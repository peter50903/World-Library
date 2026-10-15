using System.Diagnostics;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;
using Xunit.Abstractions;

namespace WorldSimulation.Core.Tests;

// TEMP diagnostic only (branch claude/c02-diag, never merged).
public sealed class DiagC02Tests
{
    private readonly ITestOutputHelper _out;
    public DiagC02Tests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DiagMeasure()
    {
        foreach (var seed in new ulong[] { 42, 7, 1, 2, 3 })
        {
            var sw = Stopwatch.StartNew();
            var report = DemoCenturyRun.Run(seed, 70, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LivingForagingSample);
            sw.Stop();
            long births = 0, natural = 0, starve = 0;
            var firstNatural = 0;
            foreach (var y in report.Years)
            {
                births += y.Births; natural += y.NaturalDeaths; starve += y.StarvationDeaths;
                if (natural > 0 && firstNatural == 0) firstNatural = y.Year;
            }
            _out.WriteLine($"DIAG seed={seed} ms={sw.ElapsedMilliseconds} errs={report.ConservationErrors.Count} firstErr={report.ConservationErrors.FirstOrDefault()} births={births} natural={natural} starve={starve} firstNaturalYear={firstNatural} pop45={report.Years[44].PopulationClosing} pop70={report.Years[69].PopulationClosing}");
        }
        var checks = M1CheckCatalog.Load(M1CheckCatalog.FindDefaultPath()!);
        var preset = new M1Preset("living", "人類繁衍", DemoWeeklyTempConfig.LivingForagingSample);
        foreach (var years in new[] { 45, 60 })
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var run = M1RunCollector.Collect(42, preset, years, TestCalendar.DaysPerYear);
                var res = M1Evaluator.Evaluate(run, checks);
                sw.Stop();
                var pop = res.Results.Single(r => r.Check.Id == "M1_POP");
                _out.WriteLine($"DIAG m1 years={years} ms={sw.ElapsedMilliseconds} abort={run.AbortReason} pop={pop.Verdict} diag={pop.Diagnosis}");
            }
            catch (Exception ex)
            {
                _out.WriteLine($"DIAG m1 years={years} EXCEPTION {ex.GetType().Name}: {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()}");
            }
        }
        Assert.Fail("diag");
    }
}
