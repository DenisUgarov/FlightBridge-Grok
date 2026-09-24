using System;
using System.Collections.Generic;
using System.Linq;
using FSMigrator;

public static class PreviewModelTests {
 public static int Main(){
  var plan=new AutomaticPlan();
  plan.Stores.Add(new Installation{Year="2020",Edition="Steam",Account="111",Root="C:\\a"});
  plan.Stores.Add(new Installation{Year="2024",Edition="Steam",Account="111",Root="C:\\b"});
  var source=Synthetic("src","Stick");
  var target=Synthetic("dst","Stick");
  var change=new Plan{Source=source,Target=target,Copied=12,Axes=3,OutputName="dst"};
  change.Skipped.Add("COCKPIT / KEY_FLIP — нет однозначного совпадения команды");
  change.Skipped.Add("MISC / KEY_X — неизвестный формат привязки");
  change.Warnings.Add("KEY_X — контекст OLD → NEW; проверьте действие в игре");
  plan.Changes.Add(change);

  var preview=PreviewModel.FromPlan(plan);
  if(!preview.CanExport)throw new Exception("CanExport expected");
  if(!preview.CanWriteToGame)throw new Exception("CanWriteToGame expected when plan Ready");
  if(preview.Items.Count!=1)throw new Exception("one item");
  var item=preview.Items[0];
  if(item.Bindings!=12||item.Axes!=3)throw new Exception("counts");
  if(item.Skipped.Count!=2)throw new Exception("skipped");
  if(item.Skipped[0].Reason!="NoTargetAction")throw new Exception("reason0="+item.Skipped[0].Reason);
  if(item.Skipped[0].Action!="FLIP")throw new Exception("action="+item.Skipped[0].Action);
  if(item.Skipped[0].Message!=null)throw new Exception("legacy Message should be null");
  if(!item.Warnings.Contains("GeneralCategoryCheck"))throw new Exception("general warn missing");
  if(!item.Warnings.Any(w=>w.IndexOf("→",StringComparison.Ordinal)>=0))throw new Exception("relocated warn missing");

  // Message preferred when set
  item.Skipped[0].Message="human from core";
  if(item.Skipped[0].Message!="human from core")throw new Exception("message");

  var multi=new MigrationPreview{Issues=new List<string>{"MultipleSteamAccounts"},CandidateSteamAccounts=new List<SteamAccount>{new SteamAccount{Id="1",HasMsfs2020=true,HasMsfs2024=true},new SteamAccount{Id="2",HasMsfs2020=true,HasMsfs2024=true}},NextStep="ChooseSteamAccount"};
  if(!PreviewModel.NeedsSteamSelection(multi))throw new Exception("steam");
  if(multi.CandidateSteamAccounts.Count!=2)throw new Exception("candidates");

  var coach=new MigrationPreview{Issues=new List<string>{"Сначала один раз сохраните пользовательский профиль управления в MSFS 2024."},NextStep="Сначала один раз сохраните пользовательский профиль управления в MSFS 2024."};
  if(!PreviewModel.NeedsFirstRunCoach(coach))throw new Exception("coach");
  if(PreviewModel.NeedsFirstRunCoach(preview))throw new Exception("no coach when items exist");

  var conf=WriteConfirmation.Confirm(preview);
  if(!object.ReferenceEquals(conf.BoundPreview,preview))throw new Exception("confirm binding");

  foreach(var lang in AppLocalization.All){
   if(lang["SaveForImport"]=="SaveForImport")throw new Exception("missing SaveForImport "+lang.Code);
   if(lang["AppsStillOpen"]=="AppsStillOpen")throw new Exception("missing AppsStillOpen "+lang.Code);
   if(lang["Reason_NoTargetAction"]=="Reason_NoTargetAction")throw new Exception("missing reason "+lang.Code);
   if(lang["NoticeSteamCloud"]=="NoticeSteamCloud")throw new Exception("missing steam notice "+lang.Code);
   if(lang["DoneTitle"]=="DoneTitle")throw new Exception("missing DoneTitle "+lang.Code);
   string.Format(lang["ConfirmWriteBody"],"list");
   string.Format(lang["GeneralWarn"],"Profile");
   string.Format(lang["StatusStore"],"MSFS 2020","Steam",3);
   // Human language audit: no technical tokens in EN/RU new strings
   foreach(var key in new[]{"ImportSteps","AppsStillOpen","NoticeSteamCloud","FallbackHint","ConfirmWriteBody","CoachSteps"}){
    string s=lang[key];
    foreach(var bad in new[]{"WGS","inputprofile","containers.index","ProductID","GUID","remote\\","1250410","2537590",".xml"}){
     if(s.IndexOf(bad,StringComparison.OrdinalIgnoreCase)>=0)throw new Exception(lang.Code+" "+key+" contains "+bad);
    }
   }
  }
  if(AppLocalization.Resolve("ru")["Restore"].IndexOf("как было",StringComparison.OrdinalIgnoreCase)<0)
   throw new Exception("RU Restore should be Вернуть как было");
  Console.WriteLine("PreviewModel, Message, Confirm, localization audit passed.");
  return 0;
 }

 static Profile Synthetic(string name,string device){
  var xml=System.Xml.Linq.XDocument.Parse("<ControllerDefinition><FriendlyName>"+name+"</FriendlyName><Device DeviceName=\""+device+"\" ProductID=\"0x1234\"><Context ContextName=\"X\"><Action ActionName=\"A\" Flag=\"1\"><Primary><KEY>1</KEY></Primary></Action></Context><Axes/></Device></ControllerDefinition>");
  return new Profile{Path="C:\\\\tmp\\\\"+name+".xml",Xml=xml,IsFragment=false,OriginalBytes=new byte[]{1,2,3}};
 }
}
