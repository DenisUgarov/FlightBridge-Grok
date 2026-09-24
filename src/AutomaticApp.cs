using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace FSMigrator {
public static class AutomaticApp {
 static bool render;
 static bool diagnosticsOnly;
 static bool transferDone;
 static Window window;
 static StackPanel content;
 static TextBlock status;
 static Button primaryTransfer;
 static MigrationPreview preview;
 static IMigrationService migration;
 static string legacyRepair;
 static AppLanguage language;
 static string backupRoot=MigrationTransaction.DefaultRoot;
 static string selectedSteamAccount;
 static string Preferences {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlightBridge","backup-root.txt");}}
 static string LanguagePreference {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlightBridge","language.txt");}}
 static string L(string key){return language[key];}
 static string F(string key,params object[] values){return string.Format(L(key),values);}
 static bool LooksLikeAppsOpen(Exception error){
  if(error==null||error.Message==null)return false;
  string m=error.Message;
  return m.IndexOf("Steam",StringComparison.OrdinalIgnoreCase)>=0
   ||m.IndexOf("FlightSimulator",StringComparison.OrdinalIgnoreCase)>=0
   ||m.IndexOf("закройте",StringComparison.OrdinalIgnoreCase)>=0
   ||m.IndexOf("Close",StringComparison.OrdinalIgnoreCase)>=0
   ||m.IndexOf("симулятор",StringComparison.OrdinalIgnoreCase)>=0;
 }
 static string ErrorText(Exception error){
  if(LooksLikeAppsOpen(error))return L("AppsStillOpen");
  if(language.Code=="ru")return error.Message;
  if(error.Message==L("StateChanged")||error.Message==L("NoBackup")||error.Message==L("WrongInstall")||error.Message=="WrongInstall")return L(error.Message=="WrongInstall"?"WrongInstall":error.Message);
  return L("OperationFailed");
 }

 static Brush Gradient(byte r1,byte g1,byte b1,byte r2,byte g2,byte b2){return new LinearGradientBrush(Color.FromRgb(r1,g1,b1),Color.FromRgb(r2,g2,b2),90);}
 static ImageSource AppIcon(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FlightBridge.png")){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}}
 static TextBlock Text(string value,int size=14,Brush color=null){var block=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,7)};if(color!=null)block.Foreground=color;return block;}
 static ControlTemplate ButtonTemplate(){
  var border=new FrameworkElementFactory(typeof(Border));border.Name="Chrome";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(135,143,154)));border.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});border.SetBinding(Border.PaddingProperty,new Binding("Padding"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
  var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);presenter.SetBinding(ContentPresenter.ContentProperty,new Binding("Content"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});border.AppendChild(presenter);
  var template=new ControlTemplate(typeof(System.Windows.Controls.Button)){VisualTree=border};var hover=new Trigger{Property=System.Windows.Controls.Button.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(UIElement.OpacityProperty,0.84,"Chrome"));template.Triggers.Add(hover);var disabled=new Trigger{Property=System.Windows.Controls.Button.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.42,"Chrome"));template.Triggers.Add(disabled);return template;
 }
 static Button Button(string label,Action action,bool primary=false){
  var button=new Button{Content=label,Padding=new Thickness(20,11,20,11),Margin=new Thickness(0,6,10,6),HorizontalAlignment=HorizontalAlignment.Left,FontSize=14,FontWeight=FontWeights.SemiBold,Template=ButtonTemplate(),Background=primary?Gradient(56,125,226,31,83,174):Gradient(250,251,252,211,216,223),Foreground=primary?Brushes.White:new SolidColorBrush(Color.FromRgb(42,49,58))};
  button.Click+=(s,e)=>{try{action();}catch(Exception ex){if(LooksLikeAppsOpen(ex))DrawAppsOpen();else ShowStatus(ErrorText(ex),true);}};return button;
 }
 static Border Card(UIElement child){return new Border{Child=child,Background=Gradient(252,253,254,228,232,237),BorderBrush=new SolidColorBrush(Color.FromRgb(174,181,190)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=new Thickness(0,9,0,9),Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=0.16,Color=Colors.Black}};}
 static void ShowStatus(string message,bool error=false){if(status==null)return;status.Text=message;status.Foreground=new SolidColorBrush(error?Color.FromRgb(166,54,54):Color.FromRgb(64,91,74));}
 static IMigrationService CreateMigration(){return new LegacyMigrationAdapter(backupRoot);}
 static TextBlock Link(string label,Action action){
  var block=new TextBlock{Margin=new Thickness(0,4,16,4),Cursor=System.Windows.Input.Cursors.Hand,FontSize=11,Opacity=0.75};
  var run=new Run(label){Foreground=new SolidColorBrush(Color.FromRgb(80,100,130)),TextDecorations=TextDecorations.Underline};
  block.Inlines.Add(run);block.MouseLeftButtonUp+=(s,e)=>{try{action();}catch(Exception ex){ShowStatus(ErrorText(ex),true);}};return block;
 }

 public static void Run(bool renderOnly,string languageOverride=null,bool diagnostics=false){
  render=renderOnly;diagnosticsOnly=diagnostics;transferDone=false;
  string selected=null;try{if(File.Exists(LanguagePreference))selected=File.ReadAllText(LanguagePreference).Trim();else{string installed=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language.txt");if(File.Exists(installed))selected=File.ReadAllText(installed).Trim();}}catch{}
  language=AppLocalization.Resolve(!string.IsNullOrWhiteSpace(languageOverride)?languageOverride:string.IsNullOrWhiteSpace(selected)?System.Globalization.CultureInfo.CurrentUICulture.Name:selected);
  try{if(File.Exists(Preferences)){string saved=File.ReadAllText(Preferences).Trim();if(Path.IsPathRooted(saved))backupRoot=saved;}}catch{}
  migration=CreateMigration();
  var app=new Application();content=new StackPanel{Margin=new Thickness(44,28,44,32)};
  var shell=new Grid{Background=Gradient(239,241,244,198,204,211)};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  var titleGrid=new Grid();titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});
  var titleRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};titleRow.Children.Add(new Image{Source=AppIcon(),Width=28,Height=28,Margin=new Thickness(0,0,9,0)});titleRow.Children.Add(new TextBlock{Text="Flight Bridge",FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(45,52,62)),VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(titleRow,1);titleGrid.Children.Add(titleRow);
  var languageBox=new ComboBox{Width=155,Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,ItemsSource=AppLocalization.All,SelectedItem=language};languageBox.SelectionChanged+=(s,e)=>ChangeLanguage(languageBox.SelectedItem as AppLanguage);Grid.SetColumn(languageBox,2);titleGrid.Children.Add(languageBox);
  var title=new Border{Background=Gradient(237,239,242,183,189,198),BorderBrush=new SolidColorBrush(Color.FromRgb(132,139,149)),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(18,8,18,8),Child=titleGrid};Grid.SetRow(title,0);shell.Children.Add(title);
  var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);shell.Children.Add(scroll);
  var footer=new Border{Background=Gradient(211,216,222,188,194,202),BorderBrush=new SolidColorBrush(Color.FromRgb(150,157,166)),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(18,8,18,8),Child=new TextBlock{Text="© 2026 Denis Ugarov",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(80,88,98)),HorizontalAlignment=HorizontalAlignment.Center}};Grid.SetRow(footer,2);shell.Children.Add(footer);
  window=new Window{Title=diagnosticsOnly?"Flight Bridge — "+L("DiagnosticsOnly"):"Flight Bridge",Width=900,Height=760,MinWidth=660,MinHeight=540,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(218,222,227)),FontFamily=new FontFamily("Segoe UI"),Foreground=new SolidColorBrush(Color.FromRgb(32,44,59)),Content=shell};
  window.Loaded+=(s,e)=>Scan();app.Run(window);
 }

 static async void Scan(){
  window.IsEnabled=false;transferDone=false;content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(L("Scanning"),18,new SolidColorBrush(Color.FromRgb(105,120,139))));
  try{
   migration=CreateMigration();
   preview=await Task.Run(()=>diagnosticsOnly?migration.Diagnose():migration.Prepare(selectedSteamAccount));
   legacyRepair=diagnosticsOnly?null:SafeLegacy();
   Draw();
  }catch(Exception ex){if(LooksLikeAppsOpen(ex))DrawAppsOpen();else DrawFailure(L("OperationFailed"),ErrorText(ex));}
  finally{window.IsEnabled=true;if(render)RenderAndClose();}
 }

 static string SafeLegacy(){try{return migration.ListBackups().Select(b=>b.Manifest).FirstOrDefault(m=>{try{return MigrationTransaction.NeedsLegacyRepair(m);}catch{return false;}});}catch{return null;}}

 static void Draw(){
  content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));
  if(transferDone){DrawDone();return;}
  if(diagnosticsOnly)content.Children.Add(Text(L("DiagnosticsBanner"),14,new SolidColorBrush(Color.FromRgb(70,100,130))));

  bool repair=legacyRepair!=null;
  bool steamPick=PreviewModel.NeedsSteamSelection(preview)||(preview!=null&&preview.CandidateSteamAccounts!=null&&preview.CandidateSteamAccounts.Count>1&&string.IsNullOrEmpty(selectedSteamAccount));
  bool coach=PreviewModel.NeedsFirstRunCoach(preview);
  bool canWrite=preview!=null&&preview.CanWriteToGame;

  if(steamPick){DrawSteamSelector();return;}
  if(coach&&!repair&&!diagnosticsOnly){DrawCoach();return;}

  DrawStatusArea();

  // Exactly one next step when present
  if(preview!=null&&!string.IsNullOrWhiteSpace(preview.NextStep)&&preview.NextStep!="ChooseSteamAccount"&&!canWrite)
   content.Children.Add(Card(Text(HumanNextStep(preview.NextStep),16,new SolidColorBrush(Color.FromRgb(50,70,100)))));

  if(diagnosticsOnly){
   if(preview!=null&&preview.Items.Count>0)DrawPreviewTable();
   status=Text(L("DiagnosticsBanner"),14);content.Children.Add(status);
   var diagActions=new WrapPanel();diagActions.Children.Add(Button(L("SaveReport"),SaveDiagnostic,true));diagActions.Children.Add(Button(L("Rescan"),Scan));content.Children.Add(diagActions);
   DrawQuietLinks();return;
  }

  content.Children.Add(Text(canWrite||repair?L("Ready"):L("Action"),22));
  if(canWrite||repair)content.Children.Add(Text(L("ReadyDescription"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));
  else if(string.IsNullOrWhiteSpace(preview!=null?preview.NextStep:null))content.Children.Add(Text(L("NoCompatible"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));

  if(canWrite||(preview!=null&&preview.Items.Count>0))DrawPreviewTable();

  status=Text(canWrite||repair?L("CloseApps"):L("NothingChanged"),14);content.Children.Add(status);

  var actions=new WrapPanel();
  if(canWrite||repair){
   primaryTransfer=Button(L("Transfer"),TransferToGame,true);
   actions.Children.Add(primaryTransfer);
  }else if(preview!=null&&preview.CanExport){
   content.Children.Add(Card(Text(string.IsNullOrEmpty(preview.FallbackReason)?L("FallbackHint"):HumanFallback(preview.FallbackReason),14)));
   actions.Children.Add(Button(L("SaveForImport"),ExportForImport,true));
   actions.Children.Add(Button(L("SaveReport"),SaveDiagnostic));
  }else{
   actions.Children.Add(Button(L("SaveReport"),SaveDiagnostic,true));
  }
  actions.Children.Add(Button(L("Rescan"),Scan));
  content.Children.Add(actions);
  content.Children.Add(Button(L("Restore"),RestoreFromBackup));
  DrawQuietLinks();

  try{
   if(migration.ListBackups().Any(b=>b.State=="Pending"||b.State=="Restoring")&&primaryTransfer!=null){primaryTransfer.IsEnabled=false;ShowStatus(L("Interrupted"),true);}
  }catch{}
 }

 static string HumanNextStep(string step){
  if(string.IsNullOrWhiteSpace(step))return L("NoCompatible");
  if(step=="ChooseSteamAccount")return L("SteamPrompt");
  // Core may send Russian prose already — show as-is if it looks human.
  if(step.IndexOf("сохраните",StringComparison.OrdinalIgnoreCase)>=0)return step;
  if(step.IndexOf("Save",StringComparison.OrdinalIgnoreCase)>=0)return step;
  return step;
 }
 static string HumanFallback(string code){
  if(code=="AutoWriteUnavailable")return L("FallbackHint");
  return string.IsNullOrEmpty(code)?L("FallbackHint"):code;
 }
 static string HumanSkipReason(SkippedBinding s){
  if(s==null)return "";
  if(!string.IsNullOrWhiteSpace(s.Message))return s.Message;
  string key="Reason_"+s.Reason;
  string mapped=L(key);
  if(mapped!=key)return mapped;
  return L("Reason_NoTargetAction");
 }

 static void DrawStatusArea(){
  var panel=new StackPanel();
  panel.Children.Add(Text(L("StatusTitle"),16));
  if(preview==null||preview.UnderlyingPlan==null||preview.UnderlyingPlan.Stores==null||preview.UnderlyingPlan.Stores.Count==0){
   panel.Children.Add(Text(L("StatusNoStores"),13,new SolidColorBrush(Color.FromRgb(105,120,139))));
  }else{
   foreach(var store in preview.UnderlyingPlan.Stores){
    string edition=store.Edition=="Steam"?"Steam":"Microsoft Store";
    panel.Children.Add(Text(F("StatusStore",store.Year=="2020"?"MSFS 2020":"MSFS 2024",edition,store.Profiles.Count),13));
   }
   if(preview.Items.Count>0)panel.Children.Add(Text(F("StatusMatched",preview.Items.Count,preview.Items.Sum(i=>i.Bindings),preview.Items.Sum(i=>i.Axes)),13,new SolidColorBrush(Color.FromRgb(64,100,80))));
   else if(preview.Issues.Count>0)panel.Children.Add(Text(L("StatusMissing"),13,new SolidColorBrush(Color.FromRgb(120,80,60))));
  }
  content.Children.Add(Card(panel));
 }

 static void DrawPreviewTable(){
  var panel=new StackPanel();
  panel.Children.Add(Text(L("PreviewTitle"),18));
  panel.Children.Add(Text(F("Profiles",preview.Items.Count)+"   ·   "+F("Assignments",preview.Items.Sum(i=>i.Bindings))+"   ·   "+F("Axes",preview.Items.Sum(i=>i.Axes)),14,new SolidColorBrush(Color.FromRgb(88,106,128))));
  foreach(var item in preview.Items){
   string device=string.IsNullOrEmpty(item.Device)?item.Category:item.Device;
   panel.Children.Add(Text(device+" · "+item.SourceProfile+" → "+item.TargetProfile+" · "+F("ColBindings").ToLowerInvariant()+": "+item.Bindings+" · "+F("ColAxes").ToLowerInvariant()+": "+item.Axes,13));
  }
  var warnings=preview.Items.SelectMany(i=>i.Warnings.Select(w=>FormatWarning(i,w))).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToList();
  if(warnings.Count>0){
   panel.Children.Add(Text(L("WarningsList"),14,new SolidColorBrush(Color.FromRgb(120,90,40))));
   foreach(var w in warnings.Take(30))panel.Children.Add(Text(w,11,new SolidColorBrush(Color.FromRgb(110,90,50))));
  }
  var skipped=preview.Items.SelectMany(i=>i.Skipped).ToList();
  if(skipped.Count>0){
   panel.Children.Add(Text(L("SkippedList"),14,new SolidColorBrush(Color.FromRgb(120,80,60))));
   foreach(var s in skipped.Take(40)){
    string label=string.IsNullOrEmpty(s.Action)?HumanSkipReason(s):(s.Action+" — "+HumanSkipReason(s));
    panel.Children.Add(Text(label,11,new SolidColorBrush(Color.FromRgb(110,90,80))));
   }
   if(skipped.Count>40)panel.Children.Add(Text("… +"+(skipped.Count-40),11));
  }
  content.Children.Add(Card(panel));
 }

 static string FormatWarning(PreviewItem item,string warning){
  if(string.Equals(warning,"GeneralCategoryCheck",StringComparison.OrdinalIgnoreCase))return F("GeneralWarn",item.TargetProfile);
  if(string.IsNullOrWhiteSpace(warning))return null;
  // Relocated KEY_* lines from Engine → plain phrase, never show codes.
  int arrow=warning.IndexOf("→",StringComparison.Ordinal);
  if(arrow<0)arrow=warning.IndexOf("->",StringComparison.Ordinal);
  if(arrow>=0)return L("Reason_ContextMismatch");
  if(warning.IndexOf("KEY_",StringComparison.OrdinalIgnoreCase)>=0)return L("Reason_ContextMismatch");
  return L("Reason_ContextMismatch");
 }

 static void DrawSteamSelector(){
  content.Children.Add(Text(L("Action"),22));
  content.Children.Add(Text(L("SteamPrompt"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));
  var accounts=(preview!=null&&preview.CandidateSteamAccounts!=null&&preview.CandidateSteamAccounts.Count>0)
   ?preview.CandidateSteamAccounts
   :migration.ListSteamAccounts().Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
  var box=new ComboBox{Width=320,Height=32,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=accounts,Margin=new Thickness(0,8,0,8)};
  if(accounts.Count>0)box.SelectedIndex=0;
  content.Children.Add(box);
  status=Text("",14);content.Children.Add(status);
  content.Children.Add(Button(L("SteamApply"),()=>{
   var pick=box.SelectedItem as SteamAccount;
   if(pick==null){ShowStatus(L("SteamPrompt"),true);return;}
   selectedSteamAccount=pick.Id;Scan();
  },true));
  DrawQuietLinks();
 }

 static void DrawCoach(){
  content.Children.Add(Text(L("CoachTitle"),22));
  string step=!string.IsNullOrWhiteSpace(preview!=null?preview.NextStep:null)?HumanNextStep(preview.NextStep):L("CoachSteps");
  content.Children.Add(Card(Text(step,15)));
  status=Text(L("NothingChanged"),14);content.Children.Add(status);
  content.Children.Add(Button(L("Rescan"),Scan,true));
  content.Children.Add(Button(L("Restore"),RestoreFromBackup));
  DrawQuietLinks();
 }

 static void DrawAppsOpen(){
  content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));
  content.Children.Add(Text(L("Action"),22));
  content.Children.Add(Card(Text(L("AppsStillOpen"),16)));
  status=Text("",14);content.Children.Add(status);
  content.Children.Add(Button(L("Rescan"),Scan,true));
  DrawQuietLinks();
 }

 static void DrawDone(){
  content.Children.Add(Text(L("DoneTitle"),22));
  content.Children.Add(Text(L("TransferDone"),15,new SolidColorBrush(Color.FromRgb(64,100,80))));
  var panel=new StackPanel();
  if(preview!=null){
   foreach(var n in preview.Notices??Enumerable.Empty<string>()){
    // Skip technical notices; show human ones only
    if(n!=null&&n.IndexOf("inputprofile",StringComparison.OrdinalIgnoreCase)<0&&n.IndexOf("\\\\",StringComparison.Ordinal)<0)
     panel.Children.Add(Text(n,13));
   }
   if(preview.HasSteamStore)panel.Children.Add(Text(L("NoticeSteamCloud"),13,new SolidColorBrush(Color.FromRgb(90,80,50))));
   if(preview.HasMicrosoftStore)panel.Children.Add(Text(L("NoticeStoreCloud"),13,new SolidColorBrush(Color.FromRgb(90,80,50))));
  }
  if(panel.Children.Count>0)content.Children.Add(Card(panel));
  status=Text(L("CloseApps"),14);content.Children.Add(status);
  content.Children.Add(Button(L("Rescan"),Scan));
  content.Children.Add(Button(L("Restore"),RestoreFromBackup));
  DrawQuietLinks();
 }

 static void DrawQuietLinks(){
  var row=new WrapPanel{Margin=new Thickness(0,22,0,0)};
  row.Children.Add(Link(L("ChangeBackup"),ChooseBackupRoot));
  row.Children.Add(Link(L("SaveReport"),SaveDiagnostic));
  if(!diagnosticsOnly)row.Children.Add(Link(L("DiagnosticsOnly"),()=>{diagnosticsOnly=true;window.Title="Flight Bridge — "+L("DiagnosticsOnly");Scan();}));
  else row.Children.Add(Link(L("ExitDiagnostics"),()=>{diagnosticsOnly=false;window.Title="Flight Bridge";Scan();}));
  content.Children.Add(row);
 }

 static void DrawFailure(string heading,string message){content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(heading,22));status=Text(message,15,new SolidColorBrush(Color.FromRgb(166,54,54)));content.Children.Add(status);content.Children.Add(Button(L("Rescan"),Scan,true));content.Children.Add(Button(L("SaveReport"),SaveDiagnostic));}
 static void ChangeLanguage(AppLanguage selected){if(selected==null||selected.Code==language.Code)return;language=selected;Directory.CreateDirectory(Path.GetDirectoryName(LanguagePreference));File.WriteAllText(LanguagePreference,language.Code);Draw();}
 static void ChooseBackupRoot(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L("BackupDescription"),SelectedPath=backupRoot})if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){backupRoot=dialog.SelectedPath;Directory.CreateDirectory(Path.GetDirectoryName(Preferences));File.WriteAllText(Preferences,backupRoot);migration=CreateMigration();ShowStatus(L("BackupChanged"));}}
 static void SaveDiagnostic(){try{string path=migration.CreateDiagnosticReport(null);ShowStatus(L("DiagnosticSaved"));}catch(Exception ex){ShowStatus(ErrorText(ex),true);}}

 static async void TransferToGame(){
  window.IsEnabled=false;try{
   if(legacyRepair!=null){await RunLegacyRepairThenRefresh();if(preview==null||!preview.CanWriteToGame)return;}
   if(preview==null||!preview.CanWriteToGame)throw new IOException(L("NothingToExport"));
   try{AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores);}
   catch(Exception ex){if(LooksLikeAppsOpen(ex)){DrawAppsOpen();return;}throw;}
   string list=string.Join("\n",preview.Items.Select(i=>"• "+(string.IsNullOrEmpty(i.Device)?i.SourceProfile:i.Device)+": "+i.SourceProfile+" → "+i.TargetProfile));
   string body=F("ConfirmWriteBody",list);
   var answer=MessageBox.Show(window,body,L("ConfirmWriteTitle"),MessageBoxButton.OKCancel,MessageBoxImage.Question);
   if(answer!=MessageBoxResult.OK){ShowStatus(L("WriteRequiresConfirm"),true);return;}
   var confirmation=WriteConfirmation.Confirm(preview);
   string result=await Task.Run(()=>migration.WriteToGame(preview,confirmation));
   transferDone=true;Draw();
  }catch(Exception ex){if(LooksLikeAppsOpen(ex))DrawAppsOpen();else ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static async void ExportForImport(){
  window.IsEnabled=false;try{
   if(preview==null||!preview.CanExport)throw new IOException(L("NothingToExport"));
   var result=await Task.Run(()=>migration.ExportForImport(preview,null));
   try{Process.Start(new ProcessStartInfo{FileName=result.Folder,UseShellExecute=true});}catch{}
   ShowStatus(L("ExportDone"));
   string tips=L("ImportSteps");
   if(preview!=null&&preview.HasSteamStore)tips+="\n\n"+L("NoticeSteamCloud");
   if(preview!=null&&preview.HasMicrosoftStore)tips+="\n\n"+L("NoticeStoreCloud");
   MessageBox.Show(window,tips,L("ExportTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static async Task RunLegacyRepairThenRefresh(){
  var info=new BackupInfo{Manifest=legacyRepair};
  await Task.Run(()=>migration.Restore(info));
  legacyRepair=null;preview=migration.Prepare(selectedSteamAccount);
 }

 static async void RestoreFromBackup(){
  if(diagnosticsOnly){ShowStatus(L("DiagnosticsBanner"),true);return;}
  window.IsEnabled=false;try{
   var backups=migration.ListBackups();
   var selected=backups.FirstOrDefault(b=>b.State=="Pending"||b.State=="Restoring")
    ??backups.FirstOrDefault(b=>b.State=="Completed"&&SafeEffective(b.Manifest));
   if(selected==null)throw new IOException(L("NoBackup"));
   await Task.Run(()=>migration.Restore(selected));
   ShowStatus(L("Restored"));MessageBox.Show(window,L("Restored"),L("RestoreTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){if(LooksLikeAppsOpen(ex))DrawAppsOpen();else ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static bool SafeEffective(string manifest){try{return MigrationTransaction.HasEffectiveChanges(manifest);}catch{return false;}}
 static void RenderAndClose(){window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create("preview-auto.png"))png.Save(file);Application.Current.Shutdown();}
}
}
