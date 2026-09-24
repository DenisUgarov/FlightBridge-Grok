using System;
using System.Collections.Generic;
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
  change.Warnings.Add("check me");
  plan.Changes.Add(change);

  var preview=PreviewModel.FromPlan(plan);
  if(!preview.CanExport)throw new Exception("CanExport expected");
  if(!preview.CanWriteToGame)throw new Exception("CanWriteToGame expected when plan Ready");
  if(preview.Items.Count!=1)throw new Exception("one item");
  var item=preview.Items[0];
  if(item.Bindings!=12||item.Axes!=3)throw new Exception("counts");
  if(item.Skipped.Count!=2)throw new Exception("skipped");
  if(item.Skipped[0].Reason!="NoTargetAction")throw new Exception("reason0="+item.Skipped[0].Reason);
  if(item.Skipped[0].Action!="KEY_FLIP")throw new Exception("action");
  if(!item.Warnings.Contains("check me"))throw new Exception("warnings");

  var multi=new MigrationPreview{Issues=new List<string>{"MultipleSteamAccounts"}};
  if(!PreviewModel.NeedsSteamSelection(multi))throw new Exception("steam");
  var coach=new MigrationPreview{Issues=new List<string>{"Сначала один раз сохраните пользовательский профиль управления в MSFS 2024."}};
  if(!PreviewModel.NeedsFirstRunCoach(coach))throw new Exception("coach");
  if(PreviewModel.NeedsFirstRunCoach(preview))throw new Exception("no coach when items exist");

  foreach(var lang in AppLocalization.All){
   if(lang["SaveForImport"]=="SaveForImport")throw new Exception("missing SaveForImport "+lang.Code);
   string.Format(lang["ConfirmWriteBody"],"list");
   string.Format(lang["ImportSteps"],"C:\\x");
   string.Format(lang["SkippedItem"],"A","B","C");
  }
  Console.WriteLine("PreviewModel and new localization keys passed.");
  return 0;
 }

 static Profile Synthetic(string name,string device){
  var xml=System.Xml.Linq.XDocument.Parse("<ControllerDefinition><FriendlyName>"+name+"</FriendlyName><Device DeviceName=\""+device+"\" ProductID=\"0x1234\"><Context ContextName=\"X\"><Action ActionName=\"A\" Flag=\"1\"><Primary><KEY>1</KEY></Primary></Action></Context><Axes/></Device></ControllerDefinition>");
  return new Profile{Path="C:\\\\tmp\\\\"+name+".xml",Xml=xml,IsFragment=false,OriginalBytes=new byte[]{1,2,3}};
 }
}
