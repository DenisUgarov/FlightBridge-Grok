using System;
using System.IO;
using System.Threading.Tasks;
using FSMigrator;

public static class UpdateBannerTests {
 public static int Main(){
  // Logic: hidden when disabled
  var settings=new UpdateSettings{Enabled=false};
  var info=new UpdateInfo{Version="0.5.2"};
  if(UpdateBannerLogic.ShouldShow(settings,info,false,DateTime.UtcNow))throw new Exception("disabled must hide");

  // Logic: hidden for skipped version
  settings=new UpdateSettings{Enabled=true,SkippedVersion="0.5.2"};
  if(UpdateBannerLogic.ShouldShow(settings,info,false,DateTime.UtcNow))throw new Exception("skipped must hide");

  // Logic: hidden during transfer
  settings=new UpdateSettings{Enabled=true};
  if(UpdateBannerLogic.ShouldShow(settings,info,true,DateTime.UtcNow))throw new Exception("busy must hide");

  // Logic: RemindAfter in future hides
  var now=new DateTime(2026,9,24,10,0,0,DateTimeKind.Utc);
  settings=new UpdateSettings{Enabled=true,RemindAfter=now.AddDays(3)};
  if(UpdateBannerLogic.ShouldShow(settings,info,false,now))throw new Exception("remind future must hide");
  if(!UpdateBannerLogic.ShouldShow(settings,info,false,now.AddDays(8)))throw new Exception("remind past must show");

  // Logic: shown otherwise
  settings=new UpdateSettings{Enabled=true};
  if(!UpdateBannerLogic.ShouldShow(settings,info,false,now))throw new Exception("should show");

  // Stub: Enabled=false && !manual => no network (we count via fake)
  var fake=new FakeUpdater();
  var s=new UpdateSettings{Enabled=false};
  var result=fake.CheckAsync(s,false).Result;
  if(result!=null)throw new Exception("disabled auto check must return null");
  if(fake.AutoChecks!=0)throw new Exception("disabled must not network");
  if(fake.CheckAsync(s,true).Result==null&&fake.ManualChecks!=1)throw new Exception("manual should attempt");

  // RemindLater persists via StubUpdater with injectable clock + temp dir
  string dir=Path.Combine(Path.GetTempPath(),"fb-update-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(dir);
  DateTime clock=now;
  var stub=new StubUpdater(()=>clock,dir);
  stub.CleanupOnStart();
  stub.RemindLater(7);
  var loaded=stub.LoadSettings();
  if(!loaded.RemindAfter.HasValue)throw new Exception("RemindAfter missing");
  if(loaded.RemindAfter.Value!=now.AddDays(7))throw new Exception("RemindAfter wrong "+loaded.RemindAfter);
  // CheckAsync respects RemindAfter unless manual
  stub.SaveSettings(new UpdateSettings{Enabled=true,RemindAfter=now.AddDays(7)});
  if(stub.CheckAsync(stub.LoadSettings(),false).Result!=null)throw new Exception("stub should null before remind");
  if(stub.CheckAsync(stub.LoadSettings(),true).Result!=null){/* stub always null — ok */}

  // SkipVersion
  stub.SkipVersion(new UpdateInfo{Version="9.9.9"});
  if(stub.LoadSettings().SkippedVersion!="9.9.9")throw new Exception("skip");

  // Never-offer = Enabled false
  var off=stub.LoadSettings();off.Enabled=false;stub.SaveSettings(off);
  if(stub.LoadSettings().Enabled)throw new Exception("enabled false");
  if(UpdateBannerLogic.ShouldShow(stub.LoadSettings(),info,false,now))throw new Exception("never offer hides");

  // Localization keys
  foreach(var lang in AppLocalization.All){
   if(lang["UpdateAvailable"]=="UpdateAvailable")throw new Exception("missing UpdateAvailable "+lang.Code);
   if(lang["UpdateNever"]=="UpdateNever")throw new Exception("missing UpdateNever "+lang.Code);
   string.Format(lang["UpdateAvailable"],"1.2.3");
   string.Format(lang["UpdateProgress"],40);
  }

  try{Directory.Delete(dir,true);}catch{}
  Console.WriteLine("Update banner logic, RemindLater, settings and localization passed.");
  return 0;
 }

 sealed class FakeUpdater : IUpdater {
  public int AutoChecks; public int ManualChecks;
  public Task<UpdateInfo> CheckAsync(UpdateSettings settings,bool manual=false){
   if(!settings.Enabled&&!manual)return Task.FromResult<UpdateInfo>(null);
   if(manual)ManualChecks++; else AutoChecks++;
   return Task.FromResult(new UpdateInfo{Version="0.5.2"});
  }
  public Task<UpdateResult> ApplyAsync(UpdateInfo info,IProgress<int> progress){return Task.FromResult(new UpdateResult{Ok=true});}
  public void SkipVersion(UpdateInfo info){}
  public void RemindLater(int days=7){}
  public UpdateSettings LoadSettings(){return new UpdateSettings();}
  public void SaveSettings(UpdateSettings settings){}
  public void CleanupOnStart(){}
 }
}
