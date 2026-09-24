using System;
using System.Collections.Generic;

namespace FSMigrator {
// Contract owned by prog/export-default-core — remove duplicates on merge.
// Temporary DTO mirror so the UI can compile against main and later repoint to Programming's types.
// Product decision (PROJECT_STATUS): WriteToGame is the main path; ExportForImport is fallback only.

public sealed class SkippedBinding {
 public string Action {get;set;}
 public string Context {get;set;}
 public string Reason {get;set;}
}

public sealed class PreviewItem {
 public string SourceProfile {get;set;}
 public string TargetProfile {get;set;}
 public string Device {get;set;}
 public string Category {get;set;}
 public string Store2020 {get;set;}
 public string Store2024 {get;set;}
 public int Bindings {get;set;}
 public int Axes {get;set;}
 public List<SkippedBinding> Skipped {get;set;}
 public List<string> Warnings {get;set;}
 public PreviewItem(){Skipped=new List<SkippedBinding>();Warnings=new List<string>();}
}

public sealed class MigrationPreview {
 public List<PreviewItem> Items {get;set;}
 public List<string> Issues {get;set;}
 public List<string> Notices {get;set;}
 public bool CanExport {get;set;}
 public bool CanWriteToGame {get;set;}
 // Temporary handle for the legacy adapter; drop when prog/export-default-core lands.
 internal AutomaticPlan UnderlyingPlan {get;set;}
 public MigrationPreview(){Items=new List<PreviewItem>();Issues=new List<string>();Notices=new List<string>();}
}

public sealed class SteamAccount {
 public string Id {get;set;}
 public string Name {get;set;}
 public bool HasMsfs2020 {get;set;}
 public bool HasMsfs2024 {get;set;}
 public override string ToString(){return (Name??Id??"?")+(HasMsfs2020&&HasMsfs2024?" · 2020+2024":HasMsfs2020?" · 2020":HasMsfs2024?" · 2024":"");}
}

public sealed class ExportResult {
 public string Folder {get;set;}
 public List<string> Files {get;set;}
 public ExportResult(){Files=new List<string>();}
}

/// <summary>Opaque token from the normal UI confirmation dialog after preview (not a red danger dialog).</summary>
public sealed class WriteConfirmation {
 public string AcknowledgedSummary {get;private set;}
 WriteConfirmation(string summary){AcknowledgedSummary=summary;}
 public static WriteConfirmation FromUiDialog(string acknowledgedSummary){
  if(string.IsNullOrWhiteSpace(acknowledgedSummary))throw new ArgumentException("Confirmation summary is required.","acknowledgedSummary");
  return new WriteConfirmation(acknowledgedSummary.Trim());
 }
}

public interface IMigrationService {
 MigrationPreview Prepare(string steamAccount);
 List<SteamAccount> ListSteamAccounts();
 ExportResult ExportForImport(MigrationPreview preview,string folder);
 string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation);
}
}
