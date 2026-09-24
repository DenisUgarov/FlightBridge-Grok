using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
namespace FSMigrator {
// Full snapshots are immutable. The journal describes ONLY files this transaction may replace.
public static class MigrationTransaction {
 public static string DefaultRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge","Backups");}}
 static string Inside(string root,string relative){if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative))throw new IOException("Недопустимый путь в копии.");string full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь выходит за пределы копии.");return full;}
 static void Write(string path,byte[] bytes){using(var s=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(bytes,0,bytes.Length);s.Flush(true);}}
 static void SaveJournal(XDocument doc,string path){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var m=new MemoryStream()){doc.Save(m);Write(temp,m.ToArray());}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 static Dictionary<string,string> Snapshot(string root){return AutoMigration.SafeFiles(root).ToDictionary(f=>f.Substring(root.TrimEnd('\\').Length+1),f=>Engine.Hash(f),StringComparer.OrdinalIgnoreCase);}
 static void Same(Dictionary<string,string> a,Dictionary<string,string> b){if(a.Count!=b.Count||a.Any(p=>!b.ContainsKey(p.Key)||b[p.Key]!=p.Value))throw new IOException("Хранилище изменилось во время операции. Повторите диагностику.");}
 static void Registered(Installation store, Profile target){
  if(store.Edition=="Steam"){
   string cache=Path.Combine(store.Root,"remotecache.vdf");string expected=Path.Combine(store.Root,"remote",Path.GetFileName(target.Path));
   if(!string.Equals(Path.GetFullPath(expected),target.Path,StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(target.Path),@"^inputprofile_(?:inputprofile_)?[0-9]+$"))throw new IOException("Неизвестное имя целевого профиля.");
   if(!File.Exists(cache)||!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(cache),"\""+System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(target.Path))+"\"\\s*\\{"))throw new IOException("Профиль не зарегистрирован в Steam Cloud.");return;
  }
  if(store.Edition=="Microsoft Store / WGS"){
   string root=Path.GetFullPath(store.Root).TrimEnd('\\');string path=Path.GetFullPath(target.Path);string prefix=root+Path.DirectorySeparatorChar;
   if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||Path.GetFileName(path)=="containers.index"||Path.GetFileName(path).StartsWith("container."))throw new IOException("Неизвестная цель в хранилище Microsoft Store.");
   string parent=Path.GetDirectoryName(path);if(!File.Exists(Path.Combine(root,"containers.index"))||!Directory.GetFiles(parent,"container.*").Any())throw new IOException("Профиль не зарегистрирован в контейнере Microsoft Store.");return;
  }
  throw new IOException("Неизвестный тип хранилища MSFS 2024.");
 }
 public static string Execute(AutomaticPlan plan,string backupRoot,Action guard,Action<int> afterWrite){
  using(var gate=new System.Threading.Mutex(false,@"Local\FlightBridgeMigration")){bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException("Другой перенос уже выполняется.");if(Sessions(backupRoot).Any(m=>State(m)=="Pending"||State(m)=="Restoring"))throw new IOException("Сначала восстановите незавершённый перенос.");return ExecuteCore(plan,backupRoot,guard,afterWrite);}finally{if(held)gate.ReleaseMutex();}}
 }
 static string ExecuteCore(AutomaticPlan plan,string backupRoot,Action guard,Action<int> afterWrite){
  if(!plan.Ready)throw new InvalidOperationException("План содержит нерешённые вопросы.");
  foreach(var p in plan.Changes){string name=Path.GetFileName(p.Target.Path);if(string.Equals(name,"remotecache.vdf",StringComparison.OrdinalIgnoreCase))throw new IOException("remotecache.vdf must never be written.");}
  guard();if(plan.Changes.Select(p=>p.Target.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=plan.Changes.Count)throw new IOException("Повторная цель переноса.");
  foreach(var p in plan.Changes){if(XNode.DeepEquals(p.Output,p.Target.Xml))throw new InvalidOperationException("Настройки уже совпадают; запись не требуется.");if(!Engine.Unchanged(p.Source)||!Engine.Unchanged(p.Target))throw new IOException("Профили изменились после проверки.");var store=plan.Stores.SingleOrDefault(s=>s.Year=="2024"&&s.Profiles.Any(t=>t.Path==p.Target.Path));if(store==null)throw new IOException("Не подтверждено целевое хранилище MSFS 2024.");Registered(store,p.Target);if(string.Equals((string)p.Target.Xml.Root.Element("FriendlyName").Attribute("Locked"),"true",StringComparison.OrdinalIgnoreCase))throw new IOException("Целевой профиль заблокирован.");}
  backupRoot=Path.GetFullPath(backupRoot);foreach(var s in plan.Stores){if(backupRoot.StartsWith(Path.GetFullPath(s.Root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||backupRoot.Equals(s.Root,StringComparison.OrdinalIgnoreCase))throw new IOException("Копии должны находиться вне хранилищ игры.");}
  Directory.CreateDirectory(backupRoot);AutoMigration.CheckPath(backupRoot);string session=Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session);
  var journal=new XDocument(new XElement("Transaction",new XAttribute("Version","1"),new XAttribute("AppVersion","0.5.1"),new XAttribute("State","Preparing")));string manifest=Path.Combine(session,"transaction.xml");
  var snapshots=new List<Dictionary<string,string>>();
  for(int i=0;i<plan.Stores.Count;i++){
   var store=plan.Stores[i];string root=Path.GetFullPath(store.Root).TrimEnd('\\');var snapshot=Snapshot(root);snapshots.Add(snapshot);string copy=Path.Combine(session,"store-"+i);Directory.CreateDirectory(copy);
   var element=new XElement("Store",new XAttribute("Root",root),new XAttribute("Copy","store-"+i),new XAttribute("Year",store.Year));journal.Root.Add(element);
   foreach(var entry in snapshot){string dest=Inside(copy,entry.Key);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(Inside(root,entry.Key),dest,false);if(Engine.Hash(dest)!=entry.Value)throw new IOException("Не удалось проверить резервную копию.");element.Add(new XElement("File",new XAttribute("Path",entry.Key),new XAttribute("SHA256",entry.Value)));}
   Same(snapshot,Snapshot(root));
  }
  for(int i=0;i<plan.Changes.Count;i++){
   var p=plan.Changes[i];int index=plan.Stores.FindIndex(s=>s.Profiles.Any(t=>t.Path==p.Target.Path));string staged=Path.Combine(session,"output-"+i);p.Target.Save(p.Output,staged);Profile.Read(staged);string relative=p.Target.Path.Substring(plan.Stores[index].Root.TrimEnd('\\').Length+1);
   journal.Root.Add(new XElement("Change",new XAttribute("Store",index),new XAttribute("Path",relative),new XAttribute("Output","output-"+i),new XAttribute("After",Engine.Hash(staged))));
  }
  for(int i=0;i<plan.Stores.Count;i++)Same(snapshots[i],Snapshot(plan.Stores[i].Root));
  foreach(var p in plan.Changes)if(!Engine.Unchanged(p.Source)||!Engine.Unchanged(p.Target))throw new IOException("Профили изменились перед записью.");
  journal.Root.SetAttributeValue("State","Pending");SaveJournal(journal,manifest);Verify(manifest);guard();
  try {int count=0;foreach(var c in journal.Root.Elements("Change")){guard();var store=journal.Root.Elements("Store").ElementAt((int)c.Attribute("Store"));string target=Inside((string)store.Attribute("Root"),(string)c.Attribute("Path"));string before=(string)store.Elements("File").Single(f=>(string)f.Attribute("Path")== (string)c.Attribute("Path")).Attribute("SHA256");if(Engine.Hash(target)!=before)throw new IOException("Целевой профиль изменился.");Replace(target,Inside(session,(string)c.Attribute("Output")));if(Engine.Hash(target)!=(string)c.Attribute("After"))throw new IOException("Проверка записи не пройдена.");Profile.Read(target);count++;if(afterWrite!=null)afterWrite(count);}
   guard();foreach(var c in journal.Root.Elements("Change")){var st=journal.Root.Elements("Store").ElementAt((int)c.Attribute("Store"));if(Engine.Hash(Inside((string)st.Attribute("Root"),(string)c.Attribute("Path")))!=(string)c.Attribute("After"))throw new IOException("Хранилище изменилось после записи.");}
   journal.Root.SetAttributeValue("State","Completed");SaveJournal(journal,manifest);return manifest;
  }catch(Exception error){try{Restore(manifest,guard); }catch(Exception recovery){throw new IOException("Перенос остановлен. Требуется «Вернуть как было». Копия: "+manifest+". "+recovery.Message,error);}throw new IOException("Перенос отменён, исходные профили восстановлены. Копия: "+manifest,error);}
 }
 static void Replace(string target,string source){AutoMigration.CheckPath(target);string temp=target+".flightbridge-"+Guid.NewGuid().ToString("N")+".tmp";try{Write(temp,File.ReadAllBytes(source));if(Engine.Hash(temp)!=Engine.Hash(source))throw new IOException("Ошибка подготовки записи.");File.Replace(temp,target,null);}finally{if(File.Exists(temp))File.Delete(temp);}}
 static XDocument Load(string manifest){var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=64*1024*1024};using(var r=XmlReader.Create(manifest,settings)){var doc=XDocument.Load(r);if(doc.Root.Name!="Transaction"||(string)doc.Root.Attribute("Version")!="1")throw new IOException("Неизвестная копия.");return doc;}}
 public static XDocument Verify(string manifest){
  AutoMigration.CheckPath(manifest);var doc=Load(manifest);string session=Path.GetDirectoryName(Path.GetFullPath(manifest));var stores=doc.Root.Elements("Store").ToList();
  foreach(var s in stores){string copy=Inside(session,(string)s.Attribute("Copy"));AutoMigration.CheckPath(copy);var files=s.Elements("File").ToList();if(files.Select(f=>(string)f.Attribute("Path")).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Count)throw new IOException("Дубликаты в копии.");foreach(var f in files){string path=Inside(copy,(string)f.Attribute("Path"));AutoMigration.CheckPath(path);if(Engine.Hash(path)!=(string)f.Attribute("SHA256"))throw new IOException("Резервная копия повреждена. Восстановление остановлено.");}}
  var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var c in doc.Root.Elements("Change")){int i=(int)c.Attribute("Store");if(i<0||i>=stores.Count)throw new IOException("Неверный индекс копии.");var s=stores[i];string relative=(string)c.Attribute("Path");if((string)s.Attribute("Year")!="2024"||s.Elements("File").Count(f=>(string)f.Attribute("Path")==relative)!=1)throw new IOException("Неверная цель восстановления.");if(!targets.Add(Inside((string)s.Attribute("Root"),relative)))throw new IOException("Повторная цель восстановления.");}return doc;
 }
 public static void Restore(string manifest,Action guard){
  using(var gate=new System.Threading.Mutex(false,@"Local\FlightBridgeMigration")){bool held=false;try{try{held=gate.WaitOne(0);}catch(System.Threading.AbandonedMutexException){held=true;}if(!held)throw new IOException("Другой перенос уже выполняется.");RestoreCore(manifest,guard);}finally{if(held)gate.ReleaseMutex();}}
 }
 static void RestoreCore(string manifest,Action guard){
  guard();var doc=Verify(manifest);string session=Path.GetDirectoryName(Path.GetFullPath(manifest));var stores=doc.Root.Elements("Store").ToList();
  // Preflight every target before changing any: never overwrite unrelated changes made after migration.
  foreach(var c in doc.Root.Elements("Change")){var s=stores[(int)c.Attribute("Store")];string relative=(string)c.Attribute("Path");string target=Inside((string)s.Attribute("Root"),relative);AutoMigration.CheckPath(target);string before=(string)s.Elements("File").Single(f=>(string)f.Attribute("Path")==relative).Attribute("SHA256");string current=Engine.Hash(target);if(current!=before&&current!=(string)c.Attribute("After"))throw new IOException("Профиль изменён после переноса. Автоматическое восстановление остановлено, чтобы сохранить новые настройки.");}
  doc.Root.SetAttributeValue("State","Restoring");SaveJournal(doc,manifest);
  foreach(var c in doc.Root.Elements("Change")){guard();var s=stores[(int)c.Attribute("Store")];string relative=(string)c.Attribute("Path");string target=Inside((string)s.Attribute("Root"),relative);string backup=Inside(Inside(session,(string)s.Attribute("Copy")),relative);string current=Engine.Hash(target);if(current==Engine.Hash(backup))continue;if(current!=(string)c.Attribute("After"))throw new IOException("Профиль изменился во время восстановления.");Replace(target,backup);if(Engine.Hash(target)!=Engine.Hash(backup))throw new IOException("Ошибка проверки восстановления.");}
  doc.Root.SetAttributeValue("State","Restored");SaveJournal(doc,manifest);
 }
 public static string[] Sessions(string root){if(!Directory.Exists(root))return new string[0];return Directory.GetDirectories(root).Select(d=>Path.Combine(d,"transaction.xml")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();}
 public static string State(string manifest){return (string)Load(manifest).Root.Attribute("State");}
 public static bool HasEffectiveChanges(string manifest){
  var doc=Verify(manifest);var stores=doc.Root.Elements("Store").ToList();
  return doc.Root.Elements("Change").Any(c=>{var store=stores[(int)c.Attribute("Store")];string relative=(string)c.Attribute("Path");string before=(string)store.Elements("File").Single(f=>(string)f.Attribute("Path")==relative).Attribute("SHA256");return before!=(string)c.Attribute("After");});
 }
 public static bool NeedsLegacyRepair(string manifest){
  var doc=Verify(manifest);if((string)doc.Root.Attribute("State")!="Completed"||doc.Root.Attribute("AppVersion")!=null)return false;var stores=doc.Root.Elements("Store").ToList();bool effective=false;
  foreach(var c in doc.Root.Elements("Change")){var store=stores[(int)c.Attribute("Store")];string relative=(string)c.Attribute("Path");string before=(string)store.Elements("File").Single(f=>(string)f.Attribute("Path")==relative).Attribute("SHA256");if(before!=(string)c.Attribute("After"))effective=true;string target=Inside((string)store.Attribute("Root"),relative);if(!File.Exists(target)||Engine.Hash(target)!=(string)c.Attribute("After"))return false;}
  return effective;
 }
}
}
