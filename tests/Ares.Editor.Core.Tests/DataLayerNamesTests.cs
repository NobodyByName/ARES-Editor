using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>Readable names of data layers.</summary>
public sealed class DataLayerNamesTests
{
    [Fact]
    public void KnownLayerGetsItsReadableName()
    {
        Assert.Equal("Condition: Cold snap", DataLayerNames.Label("DataLayer_3D6B641041B18DF5CAFA5CBE822CC44A"));
        Assert.Equal("Lighting: Night", DataLayerNames.Label("DataLayer_52C006434D304F6D91245392A866367F"));
    }

    [Fact]
    public void UnknownLayerKeepsItsName() => Assert.Equal("DataLayer_Test", DataLayerNames.Label("DataLayer_Test"));
}
