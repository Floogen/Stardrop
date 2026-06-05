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
}