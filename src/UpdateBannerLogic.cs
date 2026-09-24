using System;

namespace FSMigrator {
/// <summary>Pure rules for when the quiet update banner may appear.</summary>
public static class UpdateBannerLogic {
 public static bool ShouldShow(UpdateSettings settings,UpdateInfo info,bool busyTransferOrRestore,DateTime utcNow){
  if(busyTransferOrRestore)return false;
  if(info==null||string.IsNullOrWhiteSpace(info.Version))return false;
  if(settings==null||!settings.Enabled)return false;
  if(!string.IsNullOrEmpty(settings.SkippedVersion)&&string.Equals(settings.SkippedVersion,info.Version,StringComparison.OrdinalIgnoreCase))return false;
  if(settings.RemindAfter.HasValue&&settings.RemindAfter.Value>utcNow)return false;
  return true;
 }
}
}
