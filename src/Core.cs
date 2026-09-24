using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FSMigrator {
public static class SkipReason {
 public const string NoTargetAction="NoTargetAction";
 public const string ContextMismatch="ContextMismatch";
 public const string CategoryMismatch="CategoryMismatch";
 public const string DeviceAmbiguous="DeviceAmbiguous";
 public const string NoTarget2024Profile="NoTarget2024Profile";
}
public sealed class SkippedBinding {
 public string Action, Context, Reason, Message;
 public SkippedBinding(){}
 public SkippedBinding(string action,string context,string reason){Action=action;Context=context;Reason=reason;Message=HumanMessage(reason,action,context);}
 public SkippedBinding(string action,string context,string reason,string message){Action=action;Context=context;Reason=reason;Message=message??HumanMessage(reason,action,context);}
 // Machine Reason stays stable for tests/UI logic; Message is plain language (no paths / GUID / ProductID / WGS / XML).
 public static string HumanMessage(string reason,string action,string context){
  string a=string.IsNullOrEmpty(action)?"this control":action;
  string c=string.IsNullOrEmpty(context)?"its section":context;
  if(reason==SkipReason.NoTargetAction) return "No matching control found in the 2024 profile for "+a+" ("+c+"). / В профиле 2024 нет подходящей команды для "+a+" ("+c+").";
  if(reason==SkipReason.ContextMismatch) return "The control "+a+" exists in 2024 but in a different section than "+c+", and it could not be moved safely. / Команда "+a+" есть в 2024, но в другом разделе, чем "+c+", и безопасно перенести её нельзя.";
  if(reason==SkipReason.CategoryMismatch) return "This binding was skipped because the profile categories do not match. / Привязка пропущена: категории профилей не совпадают.";
  if(reason==SkipReason.DeviceAmbiguous) return "Several devices matched; this binding was left unchanged. / Подходит несколько устройств; привязка не изменена.";
  if(reason==SkipReason.NoTarget2024Profile) return "No matching 2024 profile was found for this 2020 profile. / Для этого профиля 2020 не найден подходящий профиль 2024.";
  return "This binding was skipped. / Эта привязка пропущена.";
 }
}
public sealed class Profile {
 public string Path; public XDocument Xml; public bool IsFragment; public byte[] OriginalBytes;
 public string Category { get { var a=Device.Element("AircraftInfo"); return a==null?"Общее управление":((string)a.Attribute("CategoryName") ?? "Конкретный самолёт"); } }
 // Export/preview code from the 2024 target only. 2020 profiles usually lack AircraftInfo;
 // we never match pairs by category. Missing CategoryName => General (import may be rejected by the sim).
 public string CategoryCode { get { var a=Device.Element("AircraftInfo"); string n=a==null?null:((string)a.Attribute("CategoryName")); return string.IsNullOrWhiteSpace(n)?"General":n.Trim(); } }
 public XElement Device { get { return Xml.Root.Element("Device"); } }
 public string Name { get { return (string)Xml.Root.Element("FriendlyName") ?? (string)Device.Attribute("DeviceName") ?? System.IO.Path.GetFileName(Path); } }
 public override string ToString() { return Name + "  ·  " + Device.Descendants("Action").Count(a=>a.Elements().Any()).ToString() + " назначений"; }
 public static Profile Read(string path) {
  if(new FileInfo(path).Length > 8*1024*1024) throw new InvalidDataException("Файл профиля слишком большой.");
  var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=8*1024*1024 };
  byte[] bytes; using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) { if(stream.Length>8*1024*1024)throw new InvalidDataException("Файл профиля слишком большой.");using(var m=new MemoryStream()){stream.CopyTo(m);bytes=m.ToArray();} }
  string content;using(var m=new MemoryStream(bytes))using(var reader=new StreamReader(m,Encoding.UTF8,true))content=reader.ReadToEnd();
  content=Regex.Replace(content,@"\A\s*<\?xml\s[^?]*\?>", "",RegexOptions.CultureInvariant);
  settings.ConformanceLevel=ConformanceLevel.Fragment;
  var container=new XElement("ProfileFragment");
  using(var r=XmlReader.Create(new StringReader(content),settings)) {while(!r.EOF){if(r.NodeType==XmlNodeType.Element)container.Add(XNode.ReadFrom(r));else if(r.NodeType==XmlNodeType.Text && !string.IsNullOrWhiteSpace(r.Value))throw new InvalidDataException("Посторонний текст в профиле.");else r.Read();}}
  bool fragment=container.Elements("Device").Any();
  XDocument doc=fragment?new XDocument(container):new XDocument(container.Elements().Single());
  if(doc.Root==null || doc.Root.Name.NamespaceName!="" || doc.Root.Elements("Device").Count()!=1 || (!doc.Root.Descendants("Context").Any() && !doc.Root.Descendants("Axes").Any())) throw new InvalidDataException("Не найден поддерживаемый XML-профиль управления MSFS.");
  return new Profile {Path=System.IO.Path.GetFullPath(path),Xml=doc,IsFragment=fragment,OriginalBytes=bytes};
 }
 public void Save(XDocument document,string path) {
  if(!IsFragment){document.Save(path);return;}
  using(var writer=new StreamWriter(path,false,new UTF8Encoding(false))) {
   writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
   foreach(var node in document.Root.Nodes())writer.WriteLine(node.ToString());
  }
 }
}
public sealed class Plan {
 public Profile Source,Target; public XDocument Output; public int Copied, Axes, Relocated; public List<string> Skipped=new List<string>(); public List<string> Warnings=new List<string>(); public List<SkippedBinding> SkippedBindings=new List<SkippedBinding>(); public string OutputName;
 public string Report { get { return Source.Name+" → "+Target.Category+"\r\nНовое имя: "+OutputName+"\r\nБудет перенесено: " + Copied + " назначений. Настройки осей: " + Axes + ".\r\nВ другом контексте: "+Relocated+". Требуют ручной настройки: " + Skipped.Count + ".\r\n\r\n" + string.Join("\r\n",Warnings.Concat(Skipped)); } }
}
public static class Engine {
 static string Attr(XElement e,string key) {return (string)e.Attribute(key) ?? "";}
 static long Id(string s) { long n; if(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) return long.TryParse(s.Substring(2),System.Globalization.NumberStyles.HexNumber,null,out n)?n:-1; return long.TryParse(s,out n)?n:-1; }
 public static Plan Analyze(Profile source,Profile target,bool allowRelocated=false) {
  if(source.Path.Equals(target.Path,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Выберите два разных профиля: из 2020 и из 2024.");
  string sp=Attr(source.Device,"ProductID"),tp=Attr(target.Device,"ProductID");
  if(Id(sp)<0 || Id(sp)!=Id(tp)) throw new InvalidDataException("Профили принадлежат разным устройствам или не содержат ProductID. Выберите профиль того же джойстика.");
  string sn=Attr(source.Device,"DeviceName"),tn=Attr(target.Device,"DeviceName");
  if(!string.Equals(sn,tn,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Названия устройств различаются. Для безопасного переноса нужен профиль того же устройства.");
  var p=new Plan {Source=source,Target=target,Output=new XDocument(target.Xml),OutputName=source.Name+" · FB "+DateTime.Now.ToString("MMdd-HHmm")+"-"+Guid.NewGuid().ToString("N").Substring(0,4)};
  var friendly=p.Output.Root.Element("FriendlyName");if(friendly==null){friendly=new XElement("FriendlyName");p.Output.Root.AddFirst(friendly);}friendly.Value=p.OutputName;friendly.SetAttributeValue("Locked","false");
  var dest=p.Output.Root.Element("Device");
  var used=new HashSet<XElement>();
  foreach(var a in source.Device.Descendants("Action").Where(a=>a.Elements("Primary").Any() || a.Elements("Secondary").Any())) {
   string name=Attr(a,"ActionName"), ctx=Attr(a.Parent,"ContextName");
   var matches=dest.Descendants("Action").Where(x=>Attr(x,"ActionName")==name && Attr(x.Parent,"ContextName")==ctx).ToList();
   bool relocated=false;
   if(matches.Count==0 && allowRelocated && name.StartsWith("KEY_",StringComparison.Ordinal)) {matches=dest.Descendants("Action").Where(x=>Attr(x,"ActionName")==name).ToList();relocated=matches.Count==1;}
   if(matches.Count!=1 || used.Contains(matches[0]) || string.IsNullOrEmpty(name)) {
    // Same outcomes as before (binding not copied). Classify for the preview contract:
    // ContextMismatch when the action exists in 2024 but not in this context / not uniquely relocatable;
    // NoTargetAction when the action is absent or the match is otherwise unusable.
    bool existsElsewhere=dest.Descendants("Action").Any(x=>Attr(x,"ActionName")==name);
    string reason=existsElsewhere?SkipReason.ContextMismatch:SkipReason.NoTargetAction;
    p.SkippedBindings.Add(new SkippedBinding(name,ctx,reason));
    p.Skipped.Add(ctx+" / "+name+" — нет однозначного совпадения команды");continue;
   }
   int flag; if(!int.TryParse(Attr(a,"Flag"),out flag) || flag<0 || flag>65535 || !a.Descendants("KEY").Any()) {p.SkippedBindings.Add(new SkippedBinding(name,ctx,SkipReason.NoTargetAction));p.Skipped.Add(ctx+" / "+name+" — неизвестный формат привязки");continue;}
   var t=matches[0]; used.Add(t);
   t.Elements("Primary").Remove();t.Elements("Secondary").Remove();t.Elements("Axis").Remove();
   foreach(var b in a.Elements().Where(x=>x.Name=="Primary" || x.Name=="Secondary" || x.Name=="Axis")) t.Add(new XElement(b));
   // Flag, ValueEvent and Delay describe the MSFS 2024 action format.  They are
   // deliberately preserved from the registered 2024 profile.  The numeric
   // values are not compatible across the two simulator generations (for
   // example, a 2020 axis flag of 4 can be 132 or 4100 in MSFS 2024).
   // Only the user's physical bindings are transferred from the 2020 profile.
   p.Copied++;
   if(relocated){p.Relocated++;p.Warnings.Add(name+" — контекст "+ctx+" → "+Attr(t.Parent,"ContextName")+"; проверьте действие в игре");}
  }
  var sa=source.Device.Element("Axes");var da=dest.Element("Axes");
  if(sa!=null && da!=null) foreach(var a in sa.Elements("Axis")) {
   var matches=da.Elements("Axis").Where(x=>Attr(x,"AxisName")==Attr(a,"AxisName")).ToList();
   if(matches.Count==1) {foreach(var at in a.Attributes().Where(x=>matches[0].Attribute(x.Name)!=null)) matches[0].SetAttributeValue(at.Name,at.Value);p.Axes++;}
  }
  foreach(var context in dest.Elements("Context")) {
   var bindings=context.Elements("Action").SelectMany(a=>a.Elements().Where(e=>e.Name=="Primary" || e.Name=="Secondary").Where(e=>e.Descendants("KEY").Any()).Select(b=>new {Action=a,Signature=string.Join("+",b.Descendants("KEY").Select(k=>k.Value.Trim()))}));
   foreach(var group in bindings.GroupBy(b=>b.Signature).Where(g=>g.Select(x=>Attr(x.Action,"ActionName")).Distinct().Count()>1 && g.Any(x=>used.Contains(x.Action))))p.Warnings.Add("Проверьте общую кнопку/ось в "+Attr(context,"ContextName")+": "+string.Join(", ",group.Select(x=>Attr(x.Action,"ActionName")).Distinct()));
  }
  return p;
 }
 public static string Hash(string path) {using(var h=SHA256.Create()) using(var s=File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","");}
 public static bool Unchanged(Profile p) {using(var h=SHA256.Create())return Hash(p.Path)==BitConverter.ToString(h.ComputeHash(p.OriginalBytes)).Replace("-","");}
 public static string Export(Plan p,string root,bool backup) {
  if(p.Copied==0) throw new InvalidOperationException("Нет совместимых назначений. Нужен другой профиль 2024 или ручная настройка.");
  // Refuse stale plans if files changed after the preview.
  if(!Unchanged(p.Source) || !Unchanged(p.Target)) throw new IOException("Профили изменились. Выполните проверку заново.");
  string dir=System.IO.Path.Combine(root,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(dir);
  if(backup) {
   File.WriteAllBytes(System.IO.Path.Combine(dir,"original-2020.xml"),p.Source.OriginalBytes);File.WriteAllBytes(System.IO.Path.Combine(dir,"original-2024.xml"),p.Target.OriginalBytes);
   new XDocument(new XElement("Backup",new XAttribute("Created",DateTime.Now.ToString("o")),new XElement("File",new XAttribute("Name","original-2020.xml"),new XAttribute("SHA256",Hash(System.IO.Path.Combine(dir,"original-2020.xml")))),new XElement("File",new XAttribute("Name","original-2024.xml"),new XAttribute("SHA256",Hash(System.IO.Path.Combine(dir,"original-2024.xml")))))).Save(System.IO.Path.Combine(dir,"backup.xml"));
  }
  p.Target.Save(p.Output,System.IO.Path.Combine(dir,"MSFS2024-migrated.xml"));Profile.Read(System.IO.Path.Combine(dir,"MSFS2024-migrated.xml"));
  File.WriteAllText(System.IO.Path.Combine(dir,"report.txt"),p.Report+"\r\n\r\nИмпортируйте MSFS2024-migrated.xml через настройки управления MSFS 2024.\r\nДля возврата выберите прежний профиль в игре"+(backup?" либо импортируйте original-2024.xml.":". Бэкап был отключён.")+"\r\nПрограмма не меняет файлы симулятора. Проверьте оси и кнопки перед полётом.");
  return dir;
 }
 public static void Restore(string manifest,string output) {
  string dir=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifest));
  var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};
  XDocument doc; using(var r=XmlReader.Create(manifest,settings)) doc=XDocument.Load(r);
  if(doc.Root.Name!="Backup") throw new InvalidDataException("Неверная резервная копия.");
  var entry=doc.Root.Elements("File").Single(x=>(string)x.Attribute("Name")=="original-2024.xml");
  string file=System.IO.Path.Combine(dir,"original-2024.xml");
  if(Hash(file)!=(string)entry.Attribute("SHA256")) throw new InvalidDataException("Резервная копия повреждена: контрольная сумма не совпадает.");
  Profile.Read(file);if(File.Exists(output)) throw new IOException("Выберите новое имя файла для восстановления.");File.Copy(file,output);
 }
 public static IEnumerable<string> Scan(string root,int depth=0,CancellationToken cancellation=default(CancellationToken)) {
  cancellation.ThrowIfCancellationRequested();
  if(depth>8 || !Directory.Exists(root)) yield break;
  string[] files,dirs;try {files=Directory.GetFiles(root);dirs=Directory.GetDirectories(root);}catch {yield break;}
  foreach(var file in files) {cancellation.ThrowIfCancellationRequested();Profile p=null;try {if(new FileInfo(file).Length<8*1024*1024)p=Profile.Read(file);}catch {} if(p!=null)yield return file;}
  foreach(var dir in dirs) {cancellation.ThrowIfCancellationRequested();bool skip;try{skip=(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0;}catch{continue;}if(skip)continue;foreach(var file in Scan(dir,depth+1,cancellation))yield return file;}
 }
 public static List<string> AutoRoots() {
  var roots=new List<string>();string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  roots.Add(System.IO.Path.Combine(local,@"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\SystemAppData\wgs"));
  string steam=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
  if(steam==null)steam=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");
  string users=System.IO.Path.Combine(steam,"userdata");if(Directory.Exists(users))foreach(var u in Directory.GetDirectories(users))roots.Add(System.IO.Path.Combine(u,@"1250410\remote"));return roots;
 }
}
}

