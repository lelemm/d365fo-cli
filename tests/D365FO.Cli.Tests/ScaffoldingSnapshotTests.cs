using D365FO.Core.Scaffolding;
using System.Xml.Linq;
using Xunit;

namespace D365FO.Cli.Tests;

/// <summary>
/// Golden-file-style snapshot tests for Phase 2 + Phase 6 scaffolders.
/// Parses the output XML and asserts structural elements are present to
/// catch silent regressions if the scaffolder templates are changed.
/// </summary>
public class ScaffoldingSnapshotTests
{
    // ---- SysOperation (Phase 2) ----

    [Fact]
    public void SysOperation_contract_has_DataContractAttribute_and_class()
    {
        var doc = SysOperationScaffolder.Contract("MyContract");
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        Assert.Equal("MyContract", root.Element("Name")!.Value);
        var src = root.Element("SourceCode")!.Element("Declaration")!.Value;
        Assert.Contains("[DataContractAttribute]", src);
    }

    [Fact]
    public void SysOperation_service_has_SysEntryPoint_and_correct_extends()
    {
        var doc = SysOperationScaffolder.Service("MyService", "MyContract", "process");
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        Assert.Equal("SysOperationServiceBase", root.Element("Extends")!.Value);
        var methods = root.Element("SourceCode")!.Element("Methods")!.Elements("Method").ToList();
        Assert.True(methods.Any(m => m.Element("Source")!.Value.Contains("[SysEntryPointAttribute")));
    }

    [Fact]
    public void SysOperation_controller_extends_SysOperationServiceController()
    {
        var doc = SysOperationScaffolder.Controller("MyController", "MyService", "process");
        var root = doc.Root!;
        Assert.Equal("SysOperationServiceController", root.Element("Extends")!.Value);
        var newMethod = root.Element("SourceCode")!.Element("Methods")!
            .Elements("Method").First(m => m.Element("Name")!.Value == "new");
        Assert.Contains("classStr(MyService)", newMethod.Element("Source")!.Value);
    }

    // ---- EDT (Phase 2) ----

    [Fact]
    public void Edt_has_correct_name_and_extends()
    {
        var doc = XppScaffolder.Edt("MyEdt", "Name");
        var root = doc.Root!;
        Assert.Equal("AxEdtString", root.Name.LocalName);
        Assert.Equal("MyEdt", root.Element("Name")!.Value);
        Assert.Equal("Name", root.Element("Extends")!.Value);
    }

    [Fact]
    public void Edt_with_size_has_StringSize_element()
    {
        var doc = XppScaffolder.Edt("MyEdt", null, null, 50);
        Assert.NotNull(doc.Root!.Element("StringSize"));
        Assert.Equal("50", doc.Root.Element("StringSize")!.Value);
    }

    // ---- Enum (Phase 2) ----

    [Fact]
    public void Enum_has_IsExtensible_and_values_in_order()
    {
        var vals = new[] { new EnumValueSpec("None", 0), new EnumValueSpec("Yes", 1), new EnumValueSpec("No", 2) };
        var doc = XppScaffolder.Enum("MyEnum", vals, isExtensible: true);
        var root = doc.Root!;
        Assert.Equal("AxEnum", root.Name.LocalName);
        Assert.Equal("Yes", root.Element("IsExtensible")!.Value);
        var items = root.Element("EnumValues")!.Elements().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("0", items[0].Element("Value")!.Value);
        Assert.Equal("1", items[1].Element("Value")!.Value);
    }

    // ---- Query (Phase 2) ----

    [Fact]
    public void Query_has_root_data_source()
    {
        var doc = QueryScaffolder.Query("CustQuery", new[] { new QueryDataSourceSpec("CustTable") });
        var root = doc.Root!;
        Assert.Equal("AxQuery", root.Name.LocalName);
        Assert.Equal("CustQuery", root.Element("Name")!.Value);
        var ds = root.Element("DataSources")!.Elements().First();
        Assert.Equal("AxQuerySimpleRootDataSource", ds.Name.LocalName);
        Assert.Equal("CustTable", ds.Element("Table")!.Value);
    }

    [Fact]
    public void Query_join_produces_embedded_data_source()
    {
        var ds = new[]
        {
            new QueryDataSourceSpec("CustTable"),
            new QueryDataSourceSpec("CustTrans", ParentDs: "CustTable"),
        };
        var doc = QueryScaffolder.Query("CustJoinQuery", ds);
        var root = doc.Root!;
        var rootDs = root.Element("DataSources")!.Elements().First();
        var embedded = rootDs.Element("DataSources")!.Elements().First();
        Assert.Equal("AxQuerySimpleEmbeddedDataSource", embedded.Name.LocalName);
        Assert.Equal("CustTrans", embedded.Element("Table")!.Value);
    }

    // ---- BusinessEvent (Phase 6) ----

    [Fact]
    public void BusinessEvent_class_extends_BusinessEventsBase()
    {
        var doc = BusinessEventScaffolder.EventClass("MyEvent", "MyEventContract", "Payments");
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        Assert.Equal("BusinessEventsBase", root.Element("Extends")!.Value);
        var decl = root.Element("SourceCode")!.Element("Declaration")!.Value;
        Assert.Contains("[BusinessEvents(", decl);
        Assert.Contains("classStr(MyEvent)", decl);
        Assert.Contains("classStr(MyEventContract)", decl);
    }

    [Fact]
    public void BusinessEvent_contract_has_DataContractAttribute()
    {
        var doc = BusinessEventScaffolder.ContractClass("MyEventContract");
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        var decl = root.Element("SourceCode")!.Element("Declaration")!.Value;
        Assert.Contains("[DataContractAttribute]", decl);
    }

    // ---- RunBase (Phase 6) ----

    [Fact]
    public void RunBase_extends_RunBase_by_default()
    {
        var doc = RunBaseScaffolder.RunBaseClass("MyRunBase", false);
        var root = doc.Root!;
        Assert.Equal("RunBase", root.Element("Extends")!.Value);
    }

    [Fact]
    public void RunBase_batch_extends_RunBaseBatch_and_has_canGoBatch()
    {
        var doc = RunBaseScaffolder.RunBaseClass("MyBatch", true);
        var root = doc.Root!;
        Assert.Equal("RunBaseBatch", root.Element("Extends")!.Value);
        var methods = root.Element("SourceCode")!.Element("Methods")!.Elements("Method");
        Assert.Contains(methods, m => m.Element("Name")!.Value == "canGoBatch");
    }

    // ---- SecurityPolicy (Phase 6) ----

    [Fact]
    public void SecurityPolicy_has_constrained_table_and_query()
    {
        var doc = SecurityPolicyScaffolder.Policy("MyPolicy", "CustTable", "MyCustPolicyQuery");
        var root = doc.Root!;
        Assert.Equal("AxSecurityPolicy", root.Name.LocalName);
        Assert.Equal("CustTable", root.Element("ConstrainedTable")!.Value);
        Assert.Equal("MyCustPolicyQuery", root.Element("Query")!.Value);
    }

    // ---- CustomService (Phase 6) ----

    [Fact]
    public void CustomService_class_has_ServiceAttribute_and_SysEntryPoint()
    {
        var doc = CustomServiceScaffolder.ServiceClass("VendorService",
            new[] { new OperationSpec("lookupVendor", "void") });
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        var decl = root.Element("SourceCode")!.Element("Declaration")!.Value;
        Assert.Contains("[ServiceAttribute]", decl);
        var methods = root.Element("SourceCode")!.Element("Methods")!.Elements("Method").ToList();
        Assert.Contains(methods, m => m.Element("Name")!.Value == "lookupVendor");
        var lookupSrc = methods.First(m => m.Element("Name")!.Value == "lookupVendor").Element("Source")!.Value;
        Assert.Contains("[SysEntryPointAttribute(true)]", lookupSrc);
    }
}
