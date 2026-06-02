using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Tabkit.Tests;

/// <summary>
/// Spawns the built tabkit.exe and verifies the documented exit-code +
/// stdout contract. Requires the CLI project to be built; the test project
/// already references Tabkit.Cli's project so dotnet test triggers the build.
/// </summary>
public class CliIntegrationTests
{
    private readonly ITestOutputHelper _output;
    public CliIntegrationTests(ITestOutputHelper output) => _output = output;

    private static string CliPath
    {
        get
        {
            // tests/Tabkit.Tests/bin/Debug/net10.0/Tabkit.Tests.dll
            //   -> ../../../../../src/Tabkit.Cli/bin/Debug/net10.0/tabkit.exe
            var cursor = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(cursor, "src", "Tabkit.Cli", "bin", "Debug", "net10.0", "tabkit.exe");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(cursor);
                if (parent is null) break;
                cursor = parent.FullName;
            }
            throw new FileNotFoundException("tabkit.exe not found — build the CLI first.");
        }
    }

    private (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);

        _output.WriteLine($"exit={p.ExitCode}");
        if (stdout.Length > 0) _output.WriteLine("--- stdout ---\n" + Trim(stdout));
        if (stderr.Length > 0) _output.WriteLine("--- stderr ---\n" + Trim(stderr));
        return (p.ExitCode, stdout, stderr);
    }

    private static string Trim(string s) => s.Length > 1500 ? s[..1500] + "..." : s;

    [Fact]
    public void Bare_Invocation_Shows_Help_And_Exits_Zero_Or_One()
    {
        var r = Run("--help");
        // System.CommandLine exits 0 for --help.
        r.ExitCode.Should().Be(0);
        r.Stdout.Should().Contain("tabkit").And.Contain("audit").And.Contain("extract").And.Contain("inventory");
    }

    [Fact]
    public void Audit_Run_On_Clean_Fixture_Exits_Zero()
    {
        var r = Run("audit", "run", TestFixtures.Corpus("clean.twb"));
        r.ExitCode.Should().Be(0);
        (r.Stdout.Contains("Clean") || r.Stdout.Contains("No findings"))
            .Should().BeTrue("clean fixture should print a clean / no-findings marker");
    }

    [Fact]
    public void Audit_Run_On_Warn_Only_Fixture_Exits_One()
    {
        var r = Run("audit", "run",
            TestFixtures.Corpus(Path.Combine("one-rule-each", "aud003_deprecated_function.twb")));
        r.ExitCode.Should().Be(1);
        (r.Stdout.Contains("AUD003") || r.Stdout.Contains("deprecated"))
            .Should().BeTrue("output should reference the firing rule");
    }

    [Fact]
    public void Audit_Run_On_Error_Fixture_Exits_Two()
    {
        var r = Run("audit", "run",
            TestFixtures.Corpus(Path.Combine("one-rule-each", "aud005_broken_column_ref.twb")));
        r.ExitCode.Should().Be(2);
        (r.Stdout.Contains("AUD005") || r.Stdout.Contains("missing_col"))
            .Should().BeTrue("output should reference the firing rule");
    }

    [Fact]
    public void Audit_Run_Json_Output_Writes_Valid_Json_To_File()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-json-");
        try
        {
            var outPath = Path.Combine(tmp.FullName, "findings.json");
            var r = Run("audit", "run",
                TestFixtures.Corpus("pii-heavy.twb"),
                "--format", "json", "--out", outPath);
            r.ExitCode.Should().BeInRange(0, 2);
            File.Exists(outPath).Should().BeTrue();
            var text = File.ReadAllText(outPath);
            text.Should().StartWith("{");
            text.Should().Contain("\"findings\"");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Audit_Inspect_Lists_All_Eleven_Rules()
    {
        var r = Run("audit", "inspect");
        r.ExitCode.Should().Be(0);
        // 6 audit + 5 governance
        foreach (var id in new[] { "AUD001", "AUD002", "AUD003", "AUD004", "AUD005", "AUD006",
                                    "GOV001", "GOV002", "GOV003", "GOV004", "GOV005" })
            r.Stdout.Should().Contain(id);
    }

    [Fact]
    public void Audit_Inspect_Pack_Filter_Limits_To_Governance()
    {
        var r = Run("audit", "inspect", "--pack", "governance");
        r.ExitCode.Should().Be(0);
        r.Stdout.Should().Contain("GOV001");
        r.Stdout.Should().NotContain("AUD001");
    }

    [Fact]
    public void Audit_Run_With_Unknown_Pack_Exits_Two_Not_Silently_Zero()
    {
        // Regression for Codex R1 finding #1: an unknown --pack used to run zero
        // rules and report the workbook clean with exit 0, hiding real findings.
        // It must now error explicitly with exit 2.
        var r = Run("audit", "run", "--pack", "typo", TestFixtures.Corpus("pii-heavy.twb"));
        r.ExitCode.Should().Be(2);
        (r.Stdout + r.Stderr).Should().Contain("unknown", "error message should name the failure")
            .And.NotContain("Clean", "must NOT claim the workbook is clean");
    }

    [Fact]
    public void Audit_Inspect_With_Unknown_Pack_Exits_Two()
    {
        var r = Run("audit", "inspect", "--pack", "typo");
        r.ExitCode.Should().Be(2);
        (r.Stdout + r.Stderr).Should().Contain("unknown");
    }

    [Fact]
    public void Audit_Run_With_Mixed_Case_Pack_Still_Matches()
    {
        // Pack matching is case-insensitive — "Governance" should equal "governance".
        // Use clean.twb so a successful match yields exit 0 (zero findings).
        // An "unknown pack" failure would yield exit 2 with "unknown" in stderr.
        var r = Run("audit", "run", "--pack", "Governance", TestFixtures.Corpus("clean.twb"));
        r.ExitCode.Should().Be(0, "Governance pack on clean fixture has zero findings");
        (r.Stdout + r.Stderr).Should().NotContain("unknown");
    }

    [Fact]
    public void Extract_Validate_Bad_Yaml_Returns_Nonzero()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "bad.yml");
            File.WriteAllText(yaml, "not_a: pipeline\n");  // missing name/source/sink
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Good_Yaml_Returns_Zero()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "good.yml");
            File.WriteAllText(yaml,
                "name: roundtrip\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().Be(0);
            r.Stdout.Should().Contain("OK").And.Contain("roundtrip");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Unknown_Source_Type()
    {
        // Regression for Codex R1 finding #2: validate must surface unknown
        // source types instead of returning OK.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "bad-source.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: madeup, path: ./in.csv }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("unknown source");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Unknown_Sink_Type()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "bad-sink.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "sink: { type: madeup, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("unknown sink");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Missing_Required_Path()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "missing-path.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("path");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Scalar_String_For_Filter_In()
    {
        // Regression for Codex R1 finding #3: `in: alice` used to iterate the
        // characters of "alice" and (probably) match nothing. Must error
        // explicitly with the "scalar string" message.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "bad-filter.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: in, value: alice }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            // Spectre wraps long error lines at console width, so match on a
            // short substring rather than the exact "scalar string" phrase.
            (r.Stdout + r.Stderr).Should().Contain("must be a list");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Filter_Where_As_Map_Not_List()
    {
        // Regression for Codex R1 round-2 finding: the YAML coercion helper
        // used to silently swallow wrong shapes. A `where:` map (instead of
        // a list of maps) used to validate OK and pass through every row at
        // run time.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "where-as-map.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: { col: name, op: ==, value: alice } }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("list of clauses");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Select_Columns_As_Scalar()
    {
        // Regression for Codex R1 round-2 finding: `columns: name` (scalar
        // string instead of a list) used to validate OK and produce a
        // zero-column CSV at run time.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "scalar-columns.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: select, columns: name }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("must be a list");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Mssql_Without_Connection_String_Or_Env()
    {
        // Regression for Codex R1 round-2 finding: mssql source with neither
        // `connection_string` nor `connection_string_env` used to validate OK
        // and only fail at Read time.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "mssql-no-conn.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: mssql, query: 'SELECT 1' }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("connection_string");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Audit_Run_With_Unknown_Format_Exits_Two_Not_Silently_Text()
    {
        // Regression for Codex R1 round-2 finding: a format typo like
        // `--format sariff` used to fall through to text and exit 0, writing
        // no file even when `--out` was given. Now errors explicitly.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-fmt-");
        try
        {
            var outPath = Path.Combine(tmp.FullName, "clean.sarif");
            var r = Run("audit", "run",
                TestFixtures.Corpus("clean.twb"),
                "--format", "sariff", "--out", outPath);
            r.ExitCode.Should().Be(2);
            (r.Stdout + r.Stderr).Should().Contain("Unknown format");
            File.Exists(outPath).Should().BeFalse("nothing must be written on a format typo");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_HasHeader_As_List()
    {
        // Regression for Codex R1 round-3 finding: `has_header: [false]`
        // (list-wrapped) used to fall through OptBool's silent default to
        // `true`, treating the first data row as headers.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "has-header-list.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv, has_header: [false] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("boolean");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Rename_Map_Value_As_List()
    {
        // Regression for Codex R1 round-3 finding: a map value that was
        // itself a YAML list used to stringify into the literal
        // "System.Collections.Generic.List`1[System.Object]" as a column name.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "rename-list-value.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: rename, columns: { name: [full_name] } }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("must be a scalar");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_NonList_Op_With_List_Value()
    {
        // Regression for Codex R1 round-3 finding: op `==` with `value: [alice]`
        // used to validate OK and filter every row out at run time because
        // only `in`/`not_in` got list-shape checks.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "eq-list.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: '==', value: [alice] }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("must be a scalar");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_HasHeader_Garbage_Without_Silent_Default()
    {
        // Defensive: `has_header: notabool` should also error rather than
        // silently falling back to the default. Round-2's parse path did the
        // wrong thing here too.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "has-header-garbage.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv, has_header: notabool }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("true / false");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Membership_Value_As_Map()
    {
        // Regression for Codex R1 round-4 finding: `in` with a map value
        // (e.g. { alice: true }) satisfied the bare IEnumerable check and
        // validated OK, then filtered every row out at run time.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "in-map.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: in, value: { alice: true } }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("not a map");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Membership_Value_With_Nested_List()
    {
        // Regression for Codex R1 round-4 finding: `in` with a nested list
        // (e.g. [[alice]]) validated OK and filtered every row out.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "in-nested.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: in, value: [[alice]] }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("nested lists or maps");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_NotIn_Value_As_Map()
    {
        // Regression for Codex R1 round-4 finding: `not_in` with a map value
        // validated OK and leaked every row (the inverse failure mode).
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "notin-map.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: not_in, value: { alice: true } }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("not a map");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Accepts_Proper_Membership_List()
    {
        // Positive control — a well-formed `in: [alice, bob]` must still pass.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "in-good.yml");
            File.WriteAllText(yaml,
                "name: ok\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: in, value: [alice, bob] }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().Be(0);
            r.Stdout.Should().Contain("OK");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Extract_Validate_Catches_Unsupported_Filter_Op()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-extract-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "bad-op.yml");
            File.WriteAllText(yaml,
                "name: bogus\n" +
                "source: { type: csv, path: ./in.csv }\n" +
                "transforms:\n" +
                "  - { type: filter, where: [{ col: name, op: matches, value: \"a.*\" }] }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "validate", yaml);
            r.ExitCode.Should().NotBe(0);
            (r.Stdout + r.Stderr).Should().Contain("unsupported filter op");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inventory_Scan_Then_Stats_Roundtrips()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-inv-");
        try
        {
            // Build a tiny workbook tree to scan
            var scanRoot = Directory.CreateDirectory(Path.Combine(tmp.FullName, "wbs")).FullName;
            File.Copy(TestFixtures.Corpus("clean.twb"), Path.Combine(scanRoot, "clean.twb"));

            var db = Path.Combine(tmp.FullName, "inv.sqlite");

            var r1 = Run("inventory", "scan", scanRoot, "--db", db);
            r1.ExitCode.Should().Be(0);
            r1.Stdout.Should().Contain("indexed 1");

            var r2 = Run("inventory", "stats", "--db", db);
            r2.ExitCode.Should().Be(0);
            r2.Stdout.Should().Contain("workbooks");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Mssql_Source_Reflective_Load_Resolves_When_Cli_Hosts_SqlClient()
    {
        // Regression for Codex R1 finding #8: the CLI must reference a SQL
        // client package so MssqlSource's reflective load resolves. If the
        // package were missing, the error path would name "Microsoft.Data.SqlClient"
        // in the message. We pass an obviously-bogus connection string so the
        // failure point comes from actually trying to connect, not from the
        // type-resolution check.
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-mssql-");
        try
        {
            var yaml = Path.Combine(tmp.FullName, "mssql.yml");
            File.WriteAllText(yaml,
                "name: probe\n" +
                "source: { type: mssql, query: 'SELECT 1', connection_string: 'Server=tcp:nonexistent.invalid,1433;Database=x;Connect Timeout=1;TrustServerCertificate=true' }\n" +
                "sink: { type: csv, path: ./out.csv }\n");
            var r = Run("extract", "run", yaml);
            r.ExitCode.Should().NotBe(0,
                "the bogus connection must fail at connect time, not at type-load time");
            (r.Stdout + r.Stderr).Should().NotContain(
                "Microsoft.Data.SqlClient",
                "the SqlClient package is referenced by the CLI; reflective load must succeed");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inventory_Stats_Without_Existing_Db_Exits_Two()
    {
        var tmp = Directory.CreateTempSubdirectory("tabkit-cli-inv-missing-");
        try
        {
            var db = Path.Combine(tmp.FullName, "does-not-exist.sqlite");
            var r = Run("inventory", "stats", "--db", db);
            r.ExitCode.Should().Be(2);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
