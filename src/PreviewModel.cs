using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FSMigrator {
/// <summary>
/// Maps AutomaticPlan into contract PreviewItem rows + NextStep / FallbackReason.
/// Easy to repoint when prog/export-default-core ships Migration.Prepare.
/// </summary>
public static class PreviewModel {
 static readonly Regex SkipLine=new Regex(@"^(?<ctx>.*?)\s*/\s*(?<action>.*?)\s+[—\-]\s+(?<reason>.*)$",RegexOptions.CultureInvariant);

 public static MigrationPreview FromPlan(AutomaticPlan plan){
  if(plan==null)throw new ArgumentNullException("plan");
  var preview=new MigrationPreview{
   UnderlyingPlan=plan,
   Issues=new List<string>(plan.Issues??new List<string>()),
   Notices=new List<string>(plan.Notices??new List<string>()),
   CanExport=plan.Changes!=null&&plan.Changes.Count>0,
   CanWriteToGame=plan.Ready,
   HasSteamStore=plan.Stores!=null&&plan.Stores.Any(s=>s.Edition=="Steam"),
   HasMicrosoftStore=plan.Stores!=null&&plan.Stores.Any(s=>s.Edition!=null&&s.Edition.IndexOf("Microsoft",StringComparison.OrdinalIgnoreCase)>=0)
  };
  string store2020=DescribeStore(plan.Stores,"2020");
  string store2024=DescribeStore(plan.Stores,"2024");
  foreach(var change in plan.Changes??new List<Plan>()){
   preview.Items.Add(new PreviewItem{
    SourceProfile=change.Source!=null?change.Source.Name:"?",
    TargetProfile=change.Target!=null?change.Target.Name:"?",
    Device=change.Source!=null?(string)change.Source.Device.Attribute("DeviceName"):"",
    Category=change.Target!=null?change.Target.Category:"",
    Store2020=store2020,
    Store2024=store2024,
    Bindings=change.Copied,
    Axes=change.Axes,
    Skipped=ParseSkipped(change.Skipped),
    Warnings=EnrichWarnings(change)
   });
  }
  FillNextStep(preview);
  return preview;
 }

 static void FillNextStep(MigrationPreview preview){
  if(preview.CanWriteToGame){preview.NextStep=null;preview.FallbackReason=null;return;}
  if(preview.Issues!=null){
   foreach(var issue in preview.Issues){
    if(issue==null)continue;
    if(string.Equals(issue,"MultipleSteamAccounts",StringComparison.OrdinalIgnoreCase)){preview.NextStep=null;return;}
    if(issue.IndexOf("сохраните пользовательский",StringComparison.OrdinalIgnoreCase)>=0
     ||(issue.IndexOf("save",StringComparison.OrdinalIgnoreCase)>=0&&issue.IndexOf("profile",StringComparison.OrdinalIgnoreCase)>=0)
     ||issue.IndexOf("пользовательский профиль управления",StringComparison.OrdinalIgnoreCase)>=0){
     preview.NextStep=issue;preview.FallbackReason=null;return;
    }
   }
   if(preview.Issues.Count>0)preview.NextStep=preview.Issues[0];
  }
  if(!preview.CanWriteToGame&&preview.CanExport)
   preview.FallbackReason="AutoWriteUnavailable";
 }

 static string DescribeStore(List<Installation> stores,string year){
  if(stores==null)return "";
  var s=stores.FirstOrDefault(x=>x.Year==year);
  if(s==null)return "";
  string edition=s.Edition=="Steam"?"Steam":"Microsoft Store";
  return "MSFS "+year+" · "+edition;
 }

 public static List<SkippedBinding> ParseSkipped(IEnumerable<string> lines){
  var result=new List<SkippedBinding>();
  if(lines==null)return result;
  foreach(var line in lines){
   if(string.IsNullOrWhiteSpace(line))continue;
   var m=SkipLine.Match(line.Trim());
   string ctx=m.Success?m.Groups["ctx"].Value.Trim():"";
   string action=m.Success?m.Groups["action"].Value.Trim():line.Trim();
   string raw=m.Success?m.Groups["reason"].Value.Trim():line.Trim();
   result.Add(new SkippedBinding{Context=ctx,Action=HumanAction(action),Reason=MapReason(raw,line),Message=null});
  }
  return result;
 }

 static string HumanAction(string action){
  if(string.IsNullOrWhiteSpace(action))return "";
  // Strip KEY_ prefix for display — keep readable command id without sounding technical.
  string a=action.Trim();
  if(a.StartsWith("KEY_",StringComparison.OrdinalIgnoreCase))a=a.Substring(4);
  return a.Replace('_',' ');
 }

 public static string MapReason(string raw,string fullLine){
  string text=(raw??"")+" "+(fullLine??"");
  if(ContainsAny(text,"нет однозначного","no unique","no unambiguous","однозначн"))return "NoTargetAction";
  if(ContainsAny(text,"неизвестный формат","unknown format","неизвестн"))return "UnknownBinding";
  if(ContainsAny(text,"контекст","context","ContextMismatch"))return "ContextMismatch";
  if(ContainsAny(text,"категор","category","CategoryMismatch"))return "CategoryMismatch";
  if(ContainsAny(text,"безопасному соответстви","безопасное соответствие","DeviceAmbiguous","неоднознач"))return "DeviceAmbiguous";
  if(ContainsAny(text,"сохраните пользовательский","NoTarget2024Profile"))return "NoTarget2024Profile";
  return "NoTargetAction";
 }

 static List<string> EnrichWarnings(Plan change){
  var list=new List<string>(change.Warnings??new List<string>());
  string cat=change.Target!=null?change.Target.Category:"";
  if(IsGeneralCategory(cat))list.Insert(0,"GeneralCategoryCheck");
  return list;
 }

 public static bool IsGeneralCategory(string category){
  if(string.IsNullOrWhiteSpace(category))return true;
  string n=category.Trim();
  return n.Equals("Общее управление",StringComparison.OrdinalIgnoreCase)
   ||n.Equals("General",StringComparison.OrdinalIgnoreCase)
   ||n.IndexOf("General",StringComparison.OrdinalIgnoreCase)>=0
   ||n.IndexOf("Общее",StringComparison.OrdinalIgnoreCase)>=0;
 }

 static bool ContainsAny(string text,params string[] needles){
  foreach(var n in needles)if(text.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0)return true;
  return false;
 }

 public static bool NeedsSteamSelection(MigrationPreview preview){
  if(preview==null)return false;
  if(preview.CandidateSteamAccounts!=null&&preview.CandidateSteamAccounts.Count>1)return true;
  return preview.Issues!=null&&preview.Issues.Any(i=>string.Equals(i,"MultipleSteamAccounts",StringComparison.OrdinalIgnoreCase));
 }

 public static bool NeedsFirstRunCoach(MigrationPreview preview){
  if(preview==null||preview.CanExport||NeedsSteamSelection(preview))return false;
  if(preview.Items!=null&&preview.Items.Count>0)return false;
  if(!string.IsNullOrWhiteSpace(preview.NextStep)){
   string n=preview.NextStep;
   if(n.IndexOf("сохраните",StringComparison.OrdinalIgnoreCase)>=0)return true;
   if(n.IndexOf("save",StringComparison.OrdinalIgnoreCase)>=0&&n.IndexOf("profile",StringComparison.OrdinalIgnoreCase)>=0)return true;
  }
  foreach(var issue in preview.Issues??new List<string>()){
   if(issue==null)continue;
   if(issue.IndexOf("сохраните пользовательский",StringComparison.OrdinalIgnoreCase)>=0)return true;
   if(issue.IndexOf("save",StringComparison.OrdinalIgnoreCase)>=0&&issue.IndexOf("profile",StringComparison.OrdinalIgnoreCase)>=0)return true;
   if(issue.IndexOf("пользовательский профиль управления",StringComparison.OrdinalIgnoreCase)>=0)return true;
  }
  return false;
 }

 /// <summary>Human warning text for UI. Prefers core human Message; maps known kinds; unknown → neutral phrase.</summary>
 public static string FormatWarning(AppLanguage lang,string targetProfile,string warning){
  if(lang==null)throw new ArgumentNullException("lang");
  if(string.IsNullOrWhiteSpace(warning))return null;
  if(string.Equals(warning,"GeneralCategoryCheck",StringComparison.OrdinalIgnoreCase))
   return string.Format(lang["GeneralWarn"],targetProfile??"");
  int arrow=warning.IndexOf("→",StringComparison.Ordinal);
  if(arrow<0)arrow=warning.IndexOf("->",StringComparison.Ordinal);
  if(arrow>=0||warning.IndexOf("KEY_",StringComparison.OrdinalIgnoreCase)>=0)
   return lang["Reason_ContextMismatch"];
  if(IsHumanWarningMessage(warning))return warning.Trim();
  return lang["Reason_CheckAssignment"];
 }
 public static bool IsHumanWarningMessage(string warning){
  if(string.IsNullOrWhiteSpace(warning))return false;
  string w=warning.Trim();
  if(w.IndexOf("KEY_",StringComparison.OrdinalIgnoreCase)>=0)return false;
  if(w.IndexOf("→",StringComparison.Ordinal)>=0||w.IndexOf("->",StringComparison.Ordinal)>=0)return false;
  if(string.Equals(w,"GeneralCategoryCheck",StringComparison.OrdinalIgnoreCase))return false;
  if(w.IndexOf(' ')<0&&w.IndexOf('—')<0&&w.Length<40&&w.All(c=>char.IsLetterOrDigit(c)||c=='_'))return false;
  return true;
 }
}
}
