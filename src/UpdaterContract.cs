using System;
using System.Threading.Tasks;

namespace FSMigrator {
// Contract owned by prog/updater — remove duplicates on merge.
// Temporary mirror so the UI compiles on main; Programming ships the real Updater.

public sealed class UpdateInfo {
 public string Version {get;set;}
 public string Notes {get;set;}
 public string AssetName {get;set;}
 public string Sha256 {get;set;}
}

public sealed class UpdateResult {
 public bool Ok {get;set;}
 public string Message {get;set;}
 public bool RestartRequired {get;set;}
}

public sealed class UpdateSettings {
 public bool Enabled {get;set;}
 public string SkippedVersion {get;set;}
 public DateTime? RemindAfter {get;set;}
 public UpdateSettings(){Enabled=true;}
}

public interface IUpdater {
 Task<UpdateInfo> CheckAsync(UpdateSettings settings,bool manual=false);
 Task<UpdateResult> ApplyAsync(UpdateInfo info,IProgress<int> progress);
 void SkipVersion(UpdateInfo info);
 void RemindLater(int days=7);
 UpdateSettings LoadSettings();
 void SaveSettings(UpdateSettings settings);
 void CleanupOnStart();
}
}
