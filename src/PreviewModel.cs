using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FSMigrator {
/// <summary>
/// Maps existing AutomaticPlan / Plan.Report data into contract PreviewItem rows.
/// Easy to delete or repoint when prog/export-default-core ships Migration.Prepare.
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
   CanWriteToGame=plan.Ready
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
    Warnings=new List<string>(change.Warnings??new List<string>())
   });
  }
  return preview;
 }

 static string DescribeStore(List<Installation> stores,string year){
  if(stores==null)return "";
  var s=stores.FirstOrDefault(x=>x.Year==year);
  if(s==null)return "";
  return "MSFS "+year+" · "+s.Edition+(string.IsNullOrEmpty(s.Account)?"":" · "+s.Account);
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
   result.Add(new SkippedBinding{Context=ctx,Action=action,Reason=MapReason(raw,line)});
  }
  return result;
 }

 public static string MapReason(string raw,string fullLine){
  string text=(raw??"")+" "+(fullLine??"");
  if(ContainsAny(text,"нет однозначного","no unique","no unambiguous","однозначн"))return "NoTargetAction";
  if(ContainsAny(text,"неизвестный формат","unknown format","неизвестн"))return "NoTargetAction";
  if(ContainsAny(text,"контекст","context","ContextMismatch"))return "ContextMismatch";
  if(ContainsAny(text,"категор","category","CategoryMismatch"))return "CategoryMismatch";
  if(ContainsAny(text,"безопасному соответстви","безопасное соответствие","DeviceAmbiguous","неоднознач"))return "DeviceAmbiguous";
  if(ContainsAny(text,"сохраните пользовательский","NoTarget2024Profile"))return "NoTarget2024Profile";
  return string.IsNullOrWhiteSpace(raw)?"NoTargetAction":raw;
 }

 static bool ContainsAny(string text,params string[] needles){
  foreach(var n in needles)if(text.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0)return true;
  return false;
 }

 public static bool NeedsSteamSelection(MigrationPreview preview){
  return preview!=null&&preview.Issues!=null&&preview.Issues.Any(i=>string.Equals(i,"MultipleSteamAccounts",StringComparison.OrdinalIgnoreCase)||(i!=null&&i.IndexOf("нескольк",StringComparison.OrdinalIgnoreCase)>=0));
 }

 public static bool NeedsFirstRunCoach(MigrationPreview preview){
  if(preview==null||preview.CanExport||NeedsSteamSelection(preview))return false;
  if(preview.Items!=null&&preview.Items.Count>0)return false;
  foreach(var issue in preview.Issues??new List<string>()){
   if(issue==null)continue;
   if(issue.IndexOf("сохраните пользовательский",StringComparison.OrdinalIgnoreCase)>=0)return true;
   if(issue.IndexOf("save",StringComparison.OrdinalIgnoreCase)>=0&&issue.IndexOf("profile",StringComparison.OrdinalIgnoreCase)>=0)return true;
   if(issue.IndexOf("пользовательский профиль управления",StringComparison.OrdinalIgnoreCase)>=0)return true;
  }
  return false;
 }
}
}
