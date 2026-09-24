using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace FSMigrator {
/// <summary>
/// Stub for prog/updater. Persists settings; CheckAsync returns null (no network).
/// Tests inject a fake IUpdater instead of this type.
/// </summary>
public sealed class StubUpdater : IUpdater {
 readonly Func<DateTime> clock;
 readonly string settingsPath;
 UpdateSettings memory;

 public StubUpdater(Func<DateTime> clock=null,string settingsDirectory=null){
  this.clock=clock??(()=>DateTime.UtcNow);
  string root=settingsDirectory;
  if(string.IsNullOrWhiteSpace(root)){
   root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FlightBridge");
   // Portable fallback: next to the exe when AppData is unavailable.
   if(string.IsNullOrWhiteSpace(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)))
    root=AppDomain.CurrentDomain.BaseDirectory??".";
  }
  Directory.CreateDirectory(root);
  settingsPath=Path.Combine(root,"update-settings.txt");
 }

 public void CleanupOnStart(){/* real updater removes leftover apply temp files */}

 public UpdateSettings LoadSettings(){
  if(memory!=null)return Clone(memory);
  var s=new UpdateSettings{Enabled=true};
  try{
   if(!File.Exists(settingsPath))return s;
   foreach(var line in File.ReadAllLines(settingsPath)){
    int i=line.IndexOf('=');if(i<=0)continue;
    string key=line.Substring(0,i).Trim();string val=line.Substring(i+1).Trim();
    if(key.Equals("Enabled",StringComparison.OrdinalIgnoreCase))s.Enabled=!(val.Equals("0")||val.Equals("false",StringComparison.OrdinalIgnoreCase));
    else if(key.Equals("SkippedVersion",StringComparison.OrdinalIgnoreCase))s.SkippedVersion=string.IsNullOrWhiteSpace(val)?null:val;
    else if(key.Equals("RemindAfter",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(val)){
     DateTime dt;if(DateTime.TryParse(val,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out dt))s.RemindAfter=dt;
    }
   }
  }catch{}
  memory=Clone(s);return s;
 }

 public void SaveSettings(UpdateSettings settings){
  if(settings==null)throw new ArgumentNullException("settings");
  memory=Clone(settings);
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
   File.WriteAllText(settingsPath,
    "Enabled="+(settings.Enabled?"1":"0")+"\r\n"+
    "SkippedVersion="+(settings.SkippedVersion??"")+"\r\n"+
    "RemindAfter="+(settings.RemindAfter.HasValue?settings.RemindAfter.Value.ToString("o",CultureInfo.InvariantCulture):"")+"\r\n");
  }catch{}
 }

 public Task<UpdateInfo> CheckAsync(UpdateSettings settings,bool manual=false){
  if(settings==null)settings=LoadSettings();
  // Enabled=false && !manual => zero network
  if(!settings.Enabled&&!manual)return Task.FromResult<UpdateInfo>(null);
  if(!manual&&settings.RemindAfter.HasValue&&settings.RemindAfter.Value>clock())return Task.FromResult<UpdateInfo>(null);
  // Stub never contacts the network.
  return Task.FromResult<UpdateInfo>(null);
 }

 public Task<UpdateResult> ApplyAsync(UpdateInfo info,IProgress<int> progress){
  if(progress!=null)progress.Report(100);
  return Task.FromResult(new UpdateResult{Ok=false,Message="stub",RestartRequired=false});
 }

 public void SkipVersion(UpdateInfo info){
  if(info==null||string.IsNullOrWhiteSpace(info.Version))return;
  var s=LoadSettings();s.SkippedVersion=info.Version;s.RemindAfter=null;SaveSettings(s);
 }

 public void RemindLater(int days=7){
  if(days<0)days=0;
  var s=LoadSettings();s.RemindAfter=clock().AddDays(days);SaveSettings(s);
 }

 static UpdateSettings Clone(UpdateSettings s){
  return new UpdateSettings{Enabled=s.Enabled,SkippedVersion=s.SkippedVersion,RemindAfter=s.RemindAfter};
 }
}
}
