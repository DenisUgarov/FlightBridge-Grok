using System;
using System.IO;

namespace FSMigrator {
/// <summary>
/// Diagnostics-only operations. Never calls WriteToGame / Restore / ExportForImport.
/// CreateDiagnosticReport already writes the file and returns its path — UI must not rewrite.
/// </summary>
public static class DiagnosticsFlow {
 public static MigrationPreview RunDiagnose(IMigrationService migration){
  if(migration==null)throw new ArgumentNullException("migration");
  return migration.Diagnose();
 }
 /// <summary>Calls CreateDiagnosticReport; returns the file path the core wrote.</summary>
 public static string SaveReport(IMigrationService migration){
  if(migration==null)throw new ArgumentNullException("migration");
  return migration.CreateDiagnosticReport(null);
 }
}
}
