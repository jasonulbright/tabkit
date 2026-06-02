using System.IO;
using System.Linq;
using FluentAssertions;
using Tabkit.Core.Inventory;

namespace Tabkit.Tests;

public class InventoryTests
{
    [Fact]
    public void Scan_Populates_Stats()
    {
        var temp = Directory.CreateTempSubdirectory("tabkit-scan-");
        try
        {
            File.Copy(TestFixtures.MinimalTwb, Path.Combine(temp.FullName, "minimal.twb"));
            var nested = Directory.CreateDirectory(Path.Combine(temp.FullName, "nested", "deeper"));
            File.Copy(TestFixtures.StressTwb, Path.Combine(nested.FullName, "stress.twb"));

            using var store = new InventoryStore();
            var result = InventoryScanner.ScanTree(temp.FullName, store);
            result.Scanned.Should().Be(2);
            result.Indexed.Should().Be(2);
            var stats = store.Stats();
            stats["workbooks"].Should().Be(2);
            stats["with_embedded_creds"].Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void EmbeddedCredentialsQuery_Finds_Stress()
    {
        var temp = Directory.CreateTempSubdirectory("tabkit-creds-");
        try
        {
            File.Copy(TestFixtures.StressTwb, Path.Combine(temp.FullName, "stress.twb"));
            using var store = new InventoryStore();
            InventoryScanner.ScanTree(temp.FullName, store);
            var rows = InventoryQueries.WorkbooksWithEmbeddedCredentials(store);
            rows.Should().NotBeEmpty();
            rows.Should().AllSatisfy(r => r["path"]!.ToString()!.Should().Contain("stress"));
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public void ReferencingTableQuery_Finds_FormulaOnlyReferences()
    {
        // Codex regression — calc formula like [Sales].[Orders].[Amount] * 2
        // should match a search for 'Orders' even though dbname is unrelated.
        var temp = Directory.CreateTempSubdirectory("tabkit-formref-");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, "formula-ref.twb"), @"<?xml version='1.0' encoding='utf-8'?>
<workbook source-build='2024.3.0' source-platform='win' version='18.1'>
  <datasources>
    <datasource caption='Other' name='federated.other' version='18.1'>
      <connection class='sqlserver' server='db.internal.example.com' dbname='Unrelated'/>
      <column datatype='integer' name='[order_id]' role='dimension' type='ordinal'/>
      <column datatype='real' name='[double_amount]' role='measure' type='quantitative'>
        <calculation class='tableau' formula='[Sales].[Orders].[Amount] * 2'/>
      </column>
    </datasource>
  </datasources>
  <worksheets>
    <worksheet name='X'><table><view><datasources>
      <datasource name='federated.other'/>
    </datasources></view></table></worksheet>
  </worksheets>
</workbook>");
            using var store = new InventoryStore();
            InventoryScanner.ScanTree(temp.FullName, store);
            var rows = InventoryQueries.WorkbooksReferencingTable(store, "Orders");
            rows.Should().NotBeEmpty();
            rows.Should().Contain(r => r["matched_via"]!.ToString() == "formula");
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public void ReferencingTableQuery_Escapes_Like_Wildcards_In_User_Input()
    {
        // Regression for Codex R1 finding #10: a search for "customer_data"
        // used to match "customerXdata" (the literal "_" was a LIKE wildcard).
        // The query now escapes `%` and `_` so the substring is treated as a
        // literal pattern.
        var temp = Directory.CreateTempSubdirectory("tabkit-likewild-");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, "likewild.twb"), @"<?xml version='1.0' encoding='utf-8'?>
<workbook source-build='2024.3.0' source-platform='win' version='18.1'>
  <datasources>
    <datasource caption='Should match' name='federated.match' version='18.1'>
      <connection class='sqlserver' server='db.internal.example.com' dbname='customer_data'/>
      <column datatype='integer' name='[id]' role='dimension' type='ordinal'/>
    </datasource>
    <datasource caption='Should NOT match' name='federated.nomatch' version='18.1'>
      <connection class='sqlserver' server='db.internal.example.com' dbname='customerXdata'/>
      <column datatype='integer' name='[id]' role='dimension' type='ordinal'/>
    </datasource>
  </datasources>
  <worksheets>
    <worksheet name='X'><table><view><datasources>
      <datasource name='federated.match'/>
    </datasources></view></table></worksheet>
  </worksheets>
</workbook>");
            using var store = new InventoryStore();
            InventoryScanner.ScanTree(temp.FullName, store);

            var rows = InventoryQueries.WorkbooksReferencingTable(store, "customer_data");
            rows.Should().Contain(r => r["dbname"]!.ToString() == "customer_data",
                "literal underscore must match");
            rows.Should().NotContain(r => r["dbname"]!.ToString() == "customerXdata",
                "underscore must be escaped — must not act as a LIKE wildcard");
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public void UsernameQuery_Catches_Fullname_And_IsUsername()
    {
        var temp = Directory.CreateTempSubdirectory("tabkit-uname-");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, "fullname.twb"), @"<?xml version='1.0' encoding='utf-8'?>
<workbook source-build='2024.3.0' source-platform='win' version='18.1'>
  <datasources>
    <datasource caption='X' name='federated.x' version='18.1'>
      <connection class='excel-direct' filename='x.xlsx'/>
      <column datatype='string' name='[a]' role='dimension' type='nominal'/>
      <column datatype='string' name='[who_full]' role='dimension' type='nominal'>
        <calculation class='tableau' formula='FULLNAME()'/>
      </column>
      <column datatype='boolean' name='[is_me]' role='dimension' type='nominal'>
        <calculation class='tableau' formula='ISUSERNAME(&quot;admin&quot;)'/>
      </column>
    </datasource>
  </datasources>
  <worksheets>
    <worksheet name='X'><table><view><datasources>
      <datasource name='federated.x'/>
    </datasources></view></table></worksheet>
  </worksheets>
</workbook>");
            using var store = new InventoryStore();
            InventoryScanner.ScanTree(temp.FullName, store);
            var rows = InventoryQueries.WorkbooksUsingUsername(store);
            var fields = rows.Select(r => r["field"]!.ToString()).ToList();
            fields.Should().Contain("[who_full]");
            fields.Should().Contain("[is_me]");
        }
        finally { temp.Delete(recursive: true); }
    }
}
