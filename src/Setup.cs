using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using System.Globalization;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;

public static class Setup {
 static string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","FlightBridge");
 static string Shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Flight Bridge.lnk");
 static SetupLanguage language=SetupLocalization.Resolve(CultureInfo.CurrentUICulture.Name);
 static string T(string key){return language[key];}
 static string[] Payloads(){return new[]{"FlightBridge.exe","README.md"};}
 static void WritePayload(string name) {
  string destination=Path.Combine(Folder,name),pending=destination+".new";Directory.CreateDirectory(Path.GetDirectoryName(destination));
  using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var file=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None)){resource.CopyTo(file);file.Flush(true);}
  if(File.Exists(destination))File.Replace(pending,destination,null);else File.Move(pending,destination);
 }
 static void CheckClosed(){if(Process.GetProcessesByName("FlightBridge").Length>0)throw new IOException(T("CloseApp"));}
 static string ReadResource(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
 static void Guide(Form owner){using(var help=new Form{Text=T("Help"),Size=new Size(820,650),MinimumSize=new Size(580,400),StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",11)}){
  var text=new TextBox{Text=ReadResource("README.md").Replace("\n","\r\n"),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White,BorderStyle=BorderStyle.None};help.Controls.Add(text);help.ShowDialog(owner);
 }}
 static void Install(){
  CheckClosed();Directory.CreateDirectory(Folder);foreach(string name in Payloads())WritePayload(name);
  File.WriteAllText(Path.Combine(Folder,"language.txt"),language.Code);
  string uninstaller=Path.Combine(Folder,"Uninstall.exe");if(!string.Equals(Assembly.GetExecutingAssembly().Location,uninstaller,StringComparison.OrdinalIgnoreCase))File.Copy(Assembly.GetExecutingAssembly().Location,uninstaller,true);
  Type shellType=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(shellType);object link=null;
  try{link=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Shortcut});Type lt=link.GetType();lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(Folder,"FlightBridge.exe")});lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Folder});lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
  using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FlightBridge")){key.SetValue("DisplayName","Flight Bridge");key.SetValue("DisplayVersion",BuildInfo.Version);key.SetValue("Publisher","Denis Ugarov");key.SetValue("InstallLocation",Folder);key.SetValue("DisplayIcon",Path.Combine(Folder,"FlightBridge.exe"));key.SetValue("UninstallString","\""+uninstaller+"\" /uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
 }
 static void StartRemove(){CheckClosed();string helper=Path.Combine(Path.GetTempPath(),"FlightBridge-remove-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);Process.Start(new ProcessStartInfo(helper,"/finish-uninstall "+Process.GetCurrentProcess().Id+" "+language.Code){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});}
 static void FinishRemove(string parentId){
  int parent;if(!int.TryParse(parentId,out parent))return;
  try{try{using(var process=Process.GetProcessById(parent)){if(!process.WaitForExit(15000))throw new IOException(T("WaitFailed"));}}catch(ArgumentException){}
   CheckClosed();if(Directory.Exists(Folder)&&(File.GetAttributes(Folder)&FileAttributes.ReparsePoint)!=0)throw new IOException(T("LinkFolder"));
   string docs=Path.Combine(Folder,"docs");if(Directory.Exists(docs)&&(File.GetAttributes(docs)&FileAttributes.ReparsePoint)!=0)throw new IOException(T("LinkFolder"));
   foreach(string name in Payloads().Concat(new[]{"Uninstall.exe","language.txt"})){string file=Path.Combine(Folder,name);if(File.Exists(file))File.Delete(file);}
   if(Directory.Exists(docs)&&Directory.GetFileSystemEntries(docs).Length==0)Directory.Delete(docs);
   if(File.Exists(Shortcut))File.Delete(Shortcut);Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FlightBridge",false);
   if(Directory.Exists(Folder)&&Directory.GetFileSystemEntries(Folder).Length==0)Directory.Delete(Folder);
   MessageBox.Show(T("Removed"),T("RemoveTitle"));
  }catch(Exception e){MessageBox.Show(e.Message,T("RemoveFailed"));}
 }
 static Form CreateForm(bool remove){
  var form=new Form{ClientSize=new Size(680,560),MinimumSize=new Size(696,599),MaximumSize=new Size(900,760),StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,BackColor=Color.FromArgb(218,222,227),Font=new Font("Segoe UI",11),AutoScaleMode=AutoScaleMode.Dpi};
  try{form.Icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);}catch{}
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(38,30,38,28),ColumnCount=1,RowCount=7,BackColor=Color.FromArgb(224,228,233)};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  for(int i=0;i<6;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  var titleRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0,0,0,18)};
  var icon=new PictureBox{Image=form.Icon==null?null:form.Icon.ToBitmap(),Size=new Size(42,42),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,12,0)};
  var heading=new Label{Text="Flight Bridge",Font=new Font("Segoe UI",25,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,4,0,0)};titleRow.Controls.Add(icon);titleRow.Controls.Add(heading);
  var languageRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,14),WrapContents=false};var languageLabel=new Label{AutoSize=true,Margin=new Padding(0,8,12,0)};var selector=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=220,Font=new Font("Segoe UI",11)};foreach(var available in SetupLocalization.All)selector.Items.Add(available);selector.SelectedItem=language;languageRow.Controls.Add(languageLabel);languageRow.Controls.Add(selector);
  var tagline=new Label{AutoSize=true,MaximumSize=new Size(590,0),Font=new Font("Segoe UI",17,FontStyle.Bold),ForeColor=Color.FromArgb(43,53,66),Margin=new Padding(0,0,0,12)};
  var description=new Label{AutoSize=true,MaximumSize=new Size(590,0),ForeColor=Color.FromArgb(70,82,98),Margin=new Padding(0,0,0,18)};
  var ready=new Label{AutoSize=true,MaximumSize=new Size(590,0),BackColor=Color.FromArgb(242,244,247),ForeColor=Color.FromArgb(55,72,94),Padding=new Padding(16,13,16,13),Margin=new Padding(0,0,0,18)};
  var location=new Label{AutoSize=true,MaximumSize=new Size(590,0),ForeColor=Color.DimGray,Margin=new Padding(0,0,0,20)};
  var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Bottom,FlowDirection=FlowDirection.RightToLeft,WrapContents=true};
  var action=new Button{AutoSize=true,Padding=new Padding(24,10,24,10),BackColor=Color.FromArgb(48,105,196),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Margin=new Padding(10,0,0,0),Font=new Font("Segoe UI",11,FontStyle.Bold)};action.FlatAppearance.BorderColor=Color.FromArgb(37,78,148);
  var cancel=new Button{AutoSize=true,Padding=new Padding(14,10,14,10),Margin=new Padding(10,0,0,0),BackColor=Color.FromArgb(238,240,243),FlatStyle=FlatStyle.Flat};cancel.FlatAppearance.BorderColor=Color.FromArgb(150,158,168);
  buttons.Controls.Add(action);buttons.Controls.Add(cancel);
  layout.Controls.Add(titleRow);layout.Controls.Add(languageRow);layout.Controls.Add(tagline);layout.Controls.Add(description);if(!remove)layout.Controls.Add(ready);layout.Controls.Add(location);layout.Controls.Add(buttons);form.Controls.Add(layout);
  Action refresh=()=>{form.Text=T(remove?"RemoveTitle":"Title");languageLabel.Text=T("Language")+":";tagline.Text=T("Tagline");description.Text=T(remove?"RemoveQuestion":"Description");ready.Text=remove?"":FSMigrator.AppLocalization.Resolve(language.Code)["ReadyDescription"];location.Text=T("Location")+": "+Folder;cancel.Text=T("Cancel");action.Text=T(remove?"Remove":"Install");};
  selector.SelectedIndexChanged+=(s,e)=>{language=(SetupLanguage)selector.SelectedItem;refresh();};refresh();
  cancel.Click+=(s,e)=>form.Close();form.CancelButton=cancel;
  action.Click+=(s,e)=>{try{action.Enabled=false;if(remove)StartRemove();else{Install();MessageBox.Show(form,T("Installed"),T("InstalledTitle"));Process.Start(new ProcessStartInfo(Path.Combine(Folder,"FlightBridge.exe")){UseShellExecute=true});}form.Close();}catch(Exception ex){MessageBox.Show(form,ex.Message,T(remove?"RemoveFailed":"InstallFailed"));action.Enabled=true;}};
  return form;
 }
 [STAThread] public static void Main(string[] args){
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Contains("/scan-test")){var report=DeviceCheck.Run();File.WriteAllText(Path.Combine(Environment.CurrentDirectory,"tests","device-check.txt"),report.Format(language));return;}
  if(args.Contains("/render-all")){string dir=Path.Combine(Environment.CurrentDirectory,"tests","screenshots");Directory.CreateDirectory(dir);foreach(var l in SetupLocalization.All){language=l;using(var form=CreateForm(false)){form.Show();Application.DoEvents();using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(dir,"setup-"+l.Code+".png"));}form.Close();}}return;}
  try{string saved=Path.Combine(Folder,"language.txt");if(File.Exists(saved))language=SetupLocalization.Resolve(File.ReadAllText(saved).Trim());}catch{}
  if(args.Length==3&&args[0]=="/finish-uninstall"){language=SetupLocalization.Resolve(args[2]);FinishRemove(args[1]);return;}
  Application.Run(CreateForm(args.Contains("/uninstall")));
 }
}

