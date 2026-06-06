namespace Stardrop.Test;

using NUnit.Framework;
using Stardrop.Models.Data.Enums;
using Stardrop.Utilities.Internal;

[TestFixture]
public class EnumParserTests
{
    [Test]
    public void GetDescription_NullEnum_ReturnsNull()
    {
        NexusServers? value = null;
        Assert.That(value.GetDescription(), Is.Null);
    }

    [Test]
    [TestCase(NexusServers.NexusCDN, "Nexus CDN")]
    [TestCase(NexusServers.LosAngeles, "Los Angeles")]
    public void GetDescription_NexusServers_WithDescription_ReturnsAttributeText(NexusServers server, string expected)
    {
        Assert.That(server.GetDescription(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(NexusServers.Chicago, "Chicago")]
    [TestCase(NexusServers.Paris, "Paris")]
    [TestCase(NexusServers.Amsterdam, "Amsterdam")]
    [TestCase(NexusServers.Prague, "Prague")]
    [TestCase(NexusServers.Miami, "Miami")]
    [TestCase(NexusServers.Singapore, "Singapore")]
    public void GetDescription_NexusServers_WithoutDescription_ReturnsMemberName(NexusServers server, string expected)
    {
        Assert.That(server.GetDescription(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(ModGrouping.ContentPack, "Content Pack")]
    [TestCase(ModGrouping.FolderCondensed, "Folder (Condensed)")]
    public void GetDescription_ModGrouping_WithDescription_ReturnsAttributeText(ModGrouping grouping, string expected)
    {
        Assert.That(grouping.GetDescription(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(ModGrouping.None, "None")]
    [TestCase(ModGrouping.Folder, "Folder")]
    public void GetDescription_ModGrouping_WithoutDescription_ReturnsMemberName(ModGrouping grouping, string expected)
    {
        Assert.That(grouping.GetDescription(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(DisplayFilter.None, "None")]
    [TestCase(DisplayFilter.ShowEnabled, "ShowEnabled")]
    [TestCase(DisplayFilter.ShowDisabled, "ShowDisabled")]
    [TestCase(DisplayFilter.RequireConfig, "RequireConfig")]
    public void GetDescription_DisplayFilter_NeverHasDescription_ReturnsMemberName(DisplayFilter filter, string expected)
    {
        Assert.That(filter.GetDescription(), Is.EqualTo(expected));
    }
}
