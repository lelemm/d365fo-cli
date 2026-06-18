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
    public void SysOperation_service_has_single_process_method_and_correct_extends()
    {
        var doc = SysOperationScaffolder.Service("MyService", "MyContract", "process");
        var root = doc.Root!;
        Assert.Equal("AxClass", root.Name.LocalName);
        Assert.Equal("SysOperationServiceBase", root.Element("Extends")!.Value);
        var methods = root.Element("SourceCode")!.Element("Methods")!.Elements("Method").ToList();
        var method = Assert.Single(methods);
        Assert.Equal("process", method.Element("Name")!.Value);
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

    [Fact]
    public void Edt_root_has_XMLSchema_instance_namespace()
    {
        // VS emits the XMLSchema-instance namespace on every AxEdt* root; without it the
        // metadata reader refuses the file. (issue #70)
        var doc = XppScaffolder.Edt("MyEdt", "Name", null, 10, "My label");
        Assert.Equal(
            "http://www.w3.org/2001/XMLSchema-instance",
            doc.Root!.GetNamespaceOfPrefix("i")!.NamespaceName);
    }

    [Fact]
    public void Edt_emits_base_members_before_derived_StringSize()
    {
        // DataContractSerializer serializes base-class members (AxEdt: Name/Extends/Label)
        // before derived members (AxEdtString.StringSize): Label must precede StringSize.
        var doc = XppScaffolder.Edt("MyEdt", "Name", null, 10, "My label");
        var names = doc.Root!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(new[] { "Name", "Extends", "Label", "StringSize" }, names);
    }

    [Fact]
    public void ScaffoldFileWriter_rejects_abstract_AxEdt_root()
    {
        var doc = new XDocument(new XElement("AxEdt", new XElement("Name", "Bad")));
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "d365fo-cli-test-abstract-edt.xml");
        var ex = Assert.Throws<System.InvalidOperationException>(() => ScaffoldFileWriter.Write(doc, tmp, overwrite: true));
        Assert.Contains("AxEdt", ex.Message);
        Assert.False(System.IO.File.Exists(tmp));
    }

    [Fact]
    public void ScaffoldFileWriter_rejects_abstract_AxEdtExtension_root()
    {
        var raw = "<?xml version=\"1.0\" encoding=\"utf-8\"?><AxEdtExtension><Name>Bad.Extension</Name></AxEdtExtension>";
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "d365fo-cli-test-abstract-edt-ext.xml");
        var ex = Assert.Throws<System.InvalidOperationException>(() => ScaffoldFileWriter.Write(raw, tmp, overwrite: true));
        Assert.Contains("AxEdtExtension", ex.Message);
        Assert.False(System.IO.File.Exists(tmp));
    }

    // ---- Enum (Phase 2) ----

    [Fact]
    public void Enum_has_IsExtensible_and_values_in_order()
    {
        var vals = new[] { new EnumValueSpec("None", 0), new EnumValueSpec("Yes", 1), new EnumValueSpec("No", 2) };
        var doc = XppScaffolder.Enum("MyEnum", vals, isExtensible: true);
        var root = doc.Root!;
        Assert.Equal("AxEnum", root.Name.LocalName);
        // IsExtensible is a CLR bool → DataContractSerializer expects true/false, not Yes/No.
        Assert.Equal("true", root.Element("IsExtensible")!.Value);
        // VS emits the XMLSchema-instance namespace on every AxEnum root.
        Assert.Equal(
            "http://www.w3.org/2001/XMLSchema-instance",
            root.GetNamespaceOfPrefix("i")!.NamespaceName);
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
    public void CustomService_class_has_ServiceAttribute()
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
        Assert.DoesNotContain("[SysEntryPointAttribute", lookupSrc);
    }
}
