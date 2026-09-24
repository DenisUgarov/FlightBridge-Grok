using System;
using System.Collections.Generic;
using FSMigrator;

public static class DiagnosticsGuardTests {
 public static int Main(){
  var fake=new FakeMigrationService();
  var preview=DiagnosticsFlow.RunDiagnose(fake);
  if(preview==null)throw new Exception("diagnose returned null");
  if(fake.DiagnoseCalls!=1)throw new Exception("DiagnoseCalls="+fake.DiagnoseCalls);
  string path=DiagnosticsFlow.SaveReport(fake);
  if(path!="C:\\reports\\diagnostics-redacted.txt")throw new Exception("must use returned path, got="+path);
  if(fake.ReportCalls!=1)throw new Exception("ReportCalls="+fake.ReportCalls);
  if(fake.WriteCalls!=0)throw new Exception("Write must not run in diagnostics");
  if(fake.RestoreCalls!=0)throw new Exception("Restore must not run in diagnostics");
  if(fake.ExportCalls!=0)throw new Exception("Export must not run in diagnostics");
  if(fake.PrepareCalls!=0)throw new Exception("Prepare must not run in diagnostics flow");
  var ru=AppLocalization.Resolve("ru");
  if(ru["DiagnosticsOnly"].IndexOf("ничего не меняет",StringComparison.Ordinal)<0)
   throw new Exception("RU DiagnosticsOnly must say ничего не меняет");
  if(ru["DiagnosticsSaveReport"]!="Сохранить отчёт")
   throw new Exception("RU save button="+ru["DiagnosticsSaveReport"]);
  if(ru["DiagnosticSaved"]!="Отчёт сохранён")
   throw new Exception("RU DiagnosticSaved="+ru["DiagnosticSaved"]);
  var en=AppLocalization.Resolve("en");
  if(en["DiagnosticsOnly"].IndexOf("changes nothing",StringComparison.OrdinalIgnoreCase)<0)
   throw new Exception("EN DiagnosticsOnly");
  if(en["DiagnosticSaved"]!="Report saved")
   throw new Exception("EN DiagnosticSaved="+en["DiagnosticSaved"]);
  Console.WriteLine("Diagnostics guard: Diagnose+SaveReport only; path from core; no write/restore/export.");
  return 0;
 }
}

sealed class FakeMigrationService : IMigrationService {
 public int PrepareCalls,DiagnoseCalls,WriteCalls,RestoreCalls,ExportCalls,ReportCalls,ListBackupCalls,ListSteamCalls;
 public MigrationPreview Prepare(string steamAccount){PrepareCalls++;return Empty();}
 public MigrationPreview Diagnose(){DiagnoseCalls++;return Empty();}
 public List<SteamAccount> ListSteamAccounts(){ListSteamCalls++;return new List<SteamAccount>();}
 public List<BackupInfo> ListBackups(){ListBackupCalls++;return new List<BackupInfo>();}
 public void Restore(BackupInfo backup){RestoreCalls++;}
 public string CreateDiagnosticReport(string folder){ReportCalls++;return "C:\\reports\\diagnostics-redacted.txt";}
 public ExportResult ExportForImport(MigrationPreview preview,string folder){ExportCalls++;return new ExportResult{Folder=folder??"."}; }
 public string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation){WriteCalls++;return "manifest";}
 static MigrationPreview Empty(){
  return new MigrationPreview{CanExport=false,CanWriteToGame=false,NextStep="Diagnostics complete.",Issues=new List<string>(),Notices=new List<string>()};
 }
}
