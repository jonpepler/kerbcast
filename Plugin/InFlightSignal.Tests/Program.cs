using System;
using System.IO;
using Kerbcast;

int failures = 0;
void Check(bool cond, string msg)
{
    if (cond) Console.WriteLine("  ok   " + msg);
    else { Console.Error.WriteLine("  FAIL " + msg); failures++; }
}

Check(InFlightSignal.Format(true) == "1", "Format(true) -> \"1\"");
Check(InFlightSignal.Format(false) == "0", "Format(false) -> \"0\"");

string dir = Path.Combine(Path.GetTempPath(), "kc_inflight_test_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
string path = Path.Combine(dir, "global.inflight");
try
{
    InFlightSignal.Write(path, true);
    Check(File.ReadAllText(path) == "1", "Write(true) leaves \"1\"");
    InFlightSignal.Write(path, false);
    Check(File.ReadAllText(path) == "0", "Write(false) overwrites to \"0\"");
    Check(!File.Exists(path + ".tmp"), "no .tmp left behind");
}
finally
{
    try { Directory.Delete(dir, true); } catch { /* best effort */ }
}

if (failures > 0) { Console.Error.WriteLine($"{failures} FAILED"); return 1; }
Console.WriteLine("all ok");
return 0;
