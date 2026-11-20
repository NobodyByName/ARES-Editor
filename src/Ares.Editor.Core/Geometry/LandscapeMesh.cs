namespace Ares.Editor.Core.Geometry;

/// <summary>
/// Triangulates landscape height-field tiles for the view at half their sample resolution: each 2 × 2 block of grid
/// cells becomes two triangles, except blocks with a hole, which keep their own cells (and leave the holes open).
/// </summary>
/// <remarks>
/// At full resolution the Dam's 704 tiles are 22.3 M triangles, more than all its objects; at half resolution they are
/// about 5.6 M, and the 1 m tiles become 2 m ones, which still shows every slope and path.
/// </remarks>
internal static class LandscapeMesh
{
    private const int Step = 2;

    /// <summary>Adds a tile's triangles to a writer.</summary>
    /// <param name="writer">The writer of the tile's group.</param>
    /// <param name="tile">The tile.</param>
    public static void Add(ChunkWriter writer, HeightFieldTile tile)
    {
        writer.BeginPart(tile.CountX * tile.CountY);
        int[] vertices = new int[tile.CountX * tile.CountY];
        Array.Fill(vertices, -1);
        int cellsX = tile.CountX - 1;
        int cellsY = tile.CountY - 1;
        for (int blockY = 0; blockY < cellsY; blockY += Step)
        {
            for (int blockX = 0; blockX < cellsX; blockX += Step)
            {
                int width = Math.Min(Step, cellsX - blockX);
                int height = Math.Min(Step, cellsY - blockY);
                if (width == Step && height == Step && !HasHole(tile, blockX, blockY))
                {
                    // Diagonal from (x + 1, y) to (x, y + 1), as a grid cell without flags.
                    int a = Vertex(blockX, blockY);
                    int b = Vertex(blockX + Step, blockY);
                    int c = Vertex(blockX, blockY + Step);
                    int d = Vertex(blockX + Step, blockY + Step);
                    writer.AddTriangle(a, b, c);
                    writer.AddTriangle(b, d, c);
                    continue;
                }

                for (int y = blockY; y < blockY + height; y++)
                {
                    for (int x = blockX; x < blockX + width; x++)
                    {
                        AddCell(x, y);
                    }
                }
            }
        }

        return;

        // Grid cell (x, y): corners a = (x, y), b = (x + 1, y), c = (x, y + 1), d = (x + 1, y + 1); its low-Y triangle holds
        // the edge a–b.
        void AddCell(int x, int y)
        {
            HeightFieldCellFlags flags = tile.CellFlags[(y * cellsX) + x];
            int a = Vertex(x, y);
            int b = Vertex(x + 1, y);
            int c = Vertex(x, y + 1);
            int d = Vertex(x + 1, y + 1);
            bool fromLowCorner = (flags & HeightFieldCellFlags.DiagonalFromLowCorner) != 0;
            if ((flags & HeightFieldCellFlags.LowYTriangleHole) == 0)
            {
                writer.AddTriangle(a, b, fromLowCorner ? d : c);
            }

            if ((flags & HeightFieldCellFlags.HighYTriangleHole) != 0)
            {
                return;
            }

            writer.AddTriangle(fromLowCorner ? a : b, d, c);
        }

        int Vertex(int x, int y)
        {
            int sample = (y * tile.CountX) + x;
            if (vertices[sample] < 0)
            {
                vertices[sample] = writer.AddVertex(
                    tile.Origin.X + (x * (double)tile.SpacingX),
                    tile.Origin.Y + (y * (double)tile.SpacingY),
                    tile.Origin.Z + (tile.Heights[sample] * (double)tile.HeightScale));
            }

            return vertices[sample];
        }
    }

    private static bool HasHole(HeightFieldTile tile, int blockX, int blockY)
    {
        const HeightFieldCellFlags Holes = HeightFieldCellFlags.LowYTriangleHole | HeightFieldCellFlags.HighYTriangleHole;
        int cellsX = tile.CountX - 1;
        for (int y = blockY; y < blockY + Step; y++)
        {
            for (int x = blockX; x < blockX + Step; x++)
            {
                if ((tile.CellFlags[(y * cellsX) + x] & Holes) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
