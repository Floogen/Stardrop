namespace Stardrop.Test;

using Stardrop.Models.Data.Enums;
using Stardrop.Utilities.Internal;

[TestClass]
public class EnumParserTests
{
    [TestMethod]
    public void GetDescription_NullEnum_ReturnsNull()
    {
        NexusServers? value = null;
        Assert.IsNull(value.GetDescription());
    }

    [TestMethod]
    [DataRow(NexusServers.NexusCDN, "Nexus CDN")]
    [DataRow(NexusServers.LosAngeles, "Los Angeles")]
    public void GetDescription_NexusServers_WithDescription_ReturnsAttributeText(NexusServers server, string expected)
    {
        Assert.AreEqual(expected, server.GetDescription());
    }

    [TestMethod]
    [DataRow(NexusServers.Chicago, "Chicago")]
    [DataRow(NexusServers.Paris, "Paris")]
    [DataRow(NexusServers.Amsterdam, "Amsterdam")]
    [DataRow(NexusServers.Prague, "Prague")]
    [DataRow(NexusServers.Miami, "Miami")]
    [DataRow(NexusServers.Singapore, "Singapore")]
    public void GetDescription_NexusServers_WithoutDescription_ReturnsMemberName(NexusServers server, string expected)
    {
        Assert.AreEqual(expected, server.GetDescription());
    }

    [TestMethod]
    [DataRow(ModGrouping.ContentPack, "Content Pack")]
    [DataRow(ModGrouping.FolderCondensed, "Folder (Condensed)")]
    public void GetDescription_ModGrouping_WithDescription_ReturnsAttributeText(ModGrouping grouping, string expected)
    {
        Assert.AreEqual(expected, grouping.GetDescription());
    }

    [TestMethod]
    [DataRow(ModGrouping.None, "None")]
    [DataRow(ModGrouping.Folder, "Folder")]
    public void GetDescription_ModGrouping_WithoutDescription_ReturnsMemberName(ModGrouping grouping, string expected)
    {
        Assert.AreEqual(expected, grouping.GetDescription());
    }

    [TestMethod]
    [DataRow(DisplayFilter.None, "None")]
    [DataRow(DisplayFilter.ShowEnabled, "ShowEnabled")]
    [DataRow(DisplayFilter.ShowDisabled, "ShowDisabled")]
    [DataRow(DisplayFilter.RequireConfig, "RequireConfig")]
    public void GetDescription_DisplayFilter_NeverHasDescription_ReturnsMemberName(DisplayFilter filter, string expected)
    {
        Assert.AreEqual(expected, filter.GetDescription());
    }
}