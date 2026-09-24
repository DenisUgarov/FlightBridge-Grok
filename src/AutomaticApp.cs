using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace FSMigrator {
public static class AutomaticApp {
 static bool render;
 static Window window;
 static StackPanel content;
 static TextBlock status;
 static Button primaryExport;
 static MigrationPreview preview;
 static IMigrationService migration;
 static string legacyRepair;
 static AppLanguage language;
 static string backupRoot=MigrationTransaction.DefaultRoot;
 static string selectedSteamAccount;
 static bool advancedOpen;
 static string Preferences {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlightBridge","backup-root.txt");}}
 static string LanguagePreference {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlightBridge","language.txt");}}
 static string L(string key){return language[key];}
 static string F(string key,params object[] values){return string.Format(L(key),values);}
 static string ErrorText(Exception error){if(language.Code=="ru")return error.Message;if(error.Message==L("StateChanged")||error.Message==L("NoBackup")||error.Message==L("WrongInstall"))return error.Message;if(error.Message.IndexOf("Steam",StringComparison.OrdinalIgnoreCase)>=0&&error.Message.IndexOf("MSFS",StringComparison.OrdinalIgnoreCase)>=0)return L("CloseApps");return L("OperationFailed");}

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
  button.Click+=(s,e)=>{try{action();}catch(Exception ex){ShowStatus(ErrorText(ex),true);}};return button;
 }
 static Button DangerButton(string label,Action action){
  var button=new Button{Content=label,Padding=new Thickness(20,11,20,11),Margin=new Thickness(0,6,10,6),HorizontalAlignment=HorizontalAlignment.Left,FontSize=14,FontWeight=FontWeights.SemiBold,Template=ButtonTemplate(),Background=Gradient(176,48,48,132,28,28),Foreground=Brushes.White};
  button.Click+=(s,e)=>{try{action();}catch(Exception ex){ShowStatus(ErrorText(ex),true);}};return button;
 }
 static Border Card(UIElement child){return new Border{Child=child,Background=Gradient(252,253,254,228,232,237),BorderBrush=new SolidColorBrush(Color.FromRgb(174,181,190)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=new Thickness(0,9,0,9),Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=0.16,Color=Colors.Black}};}
 static void ShowStatus(string message,bool error=false){if(status==null)return;status.Text=message;status.Foreground=new SolidColorBrush(error?Color.FromRgb(166,54,54):Color.FromRgb(64,91,74));}
 static IMigrationService CreateMigration(){return new LegacyMigrationAdapter(backupRoot);}

 public static void Run(bool renderOnly,string languageOverride=null){
  render=renderOnly;
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
  window=new Window{Title="Flight Bridge",Width=940,Height=800,MinWidth=700,MinHeight=580,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(218,222,227)),FontFamily=new FontFamily("Segoe UI"),Foreground=new SolidColorBrush(Color.FromRgb(32,44,59)),Content=shell};
  window.Loaded+=(s,e)=>Scan();app.Run(window);
 }

 static async void Scan(){
  window.IsEnabled=false;content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(L("Scanning"),18,new SolidColorBrush(Color.FromRgb(105,120,139))));
  try{
   migration=CreateMigration();
   preview=await Task.Run(()=>migration.Prepare(selectedSteamAccount));
   legacyRepair=SafeSessions().FirstOrDefault(m=>SafeLegacyRepair(m));
   Draw();
  }catch(Exception ex){DrawFailure(L("OperationFailed"),ErrorText(ex));}
  finally{window.IsEnabled=true;if(render)RenderAndClose();}
 }

 static void Draw(){
  content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));
  bool repair=legacyRepair!=null;
  bool steamPick=PreviewModel.NeedsSteamSelection(preview);
  bool coach=PreviewModel.NeedsFirstRunCoach(preview);
  bool ready=preview!=null&&preview.CanExport;

  if(steamPick){DrawSteamSelector();return;}
  if(coach&&!repair){DrawCoach();return;}

  content.Children.Add(Text(ready||repair?L("Ready"):L("Action"),22));
  content.Children.Add(Text(repair?(language.Code=="ru"?"Найдена запись предыдущей версии. Flight Bridge сам вернёт исходный профиль и сразу выполнит исправленный перенос.":L("ReadyDescription")):ready?L("ReadyDescription"):(language.Code=="ru"&&preview!=null?preview.Issues.FirstOrDefault():null)??L("NoCompatible"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));

  if(ready)DrawPreviewTable();

  status=Text(ready||repair?L("CloseApps"):L("NothingChanged"),14);content.Children.Add(status);

  var actions=new WrapPanel();
  primaryExport=Button(L("SaveForImport"),ExportForImport,true);
  primaryExport.IsEnabled=ready||repair;
  actions.Children.Add(primaryExport);
  actions.Children.Add(Button(L("Rescan"),Scan));
  content.Children.Add(actions);
  content.Children.Add(Button(L("Restore"),Restore));

  var advancedToggle=new CheckBox{Content=L("Advanced"),IsChecked=advancedOpen,Margin=new Thickness(0,12,0,4),FontSize=13};
  var advancedPanel=new StackPanel{Visibility=advancedOpen?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(0,4,0,8)};
  advancedPanel.Children.Add(Text(L("AdvancedHint"),12,new SolidColorBrush(Color.FromRgb(120,70,70))));
  var writeBtn=DangerButton(L("WriteToGame"),WriteDirect);writeBtn.IsEnabled=(preview!=null&&preview.CanWriteToGame)||repair;
  advancedPanel.Children.Add(writeBtn);
  advancedToggle.Checked+=(s,e)=>{advancedOpen=true;advancedPanel.Visibility=Visibility.Visible;};
  advancedToggle.Unchecked+=(s,e)=>{advancedOpen=false;advancedPanel.Visibility=Visibility.Collapsed;};
  content.Children.Add(advancedToggle);content.Children.Add(advancedPanel);

  var details=new StackPanel();
  if(preview!=null&&preview.UnderlyingPlan!=null){
   foreach(var store in preview.UnderlyingPlan.Stores)details.Children.Add(Text(F("GameProfiles",store.Year=="2020"?"MSFS 2020":"MSFS 2024",store.Edition=="Steam"?"Steam":"Microsoft Store",store.Profiles.Count),13));
   foreach(var item in preview.Items)details.Children.Add(Text(F("ChangeResult",item.SourceProfile,item.TargetProfile,item.Bindings,item.Axes),13));
   if(language.Code=="ru")foreach(var notice in preview.Notices)details.Children.Add(Text(notice,12,new SolidColorBrush(Color.FromRgb(105,120,139))));
  }
  content.Children.Add(new Expander{Header=L("Details"),Content=details,Margin=new Thickness(0,14,0,4)});
  var backup=new StackPanel();backup.Children.Add(Text(L("BackupStored"),13));backup.Children.Add(Text(backupRoot,12,new SolidColorBrush(Color.FromRgb(105,120,139))));backup.Children.Add(Button(L("ChangeBackup"),ChooseBackupRoot));backup.Children.Add(Button(L("SaveReport"),SaveDiagnostic));
  content.Children.Add(new Expander{Header=L("BackupSection"),Content=backup,Margin=new Thickness(0,4,0,0)});
  string pending=SafeSessions().FirstOrDefault(m=>SafeState(m)=="Pending"||SafeState(m)=="Restoring");if(pending!=null){primaryExport.IsEnabled=false;ShowStatus(L("Interrupted"),true);}
 }

 static void DrawPreviewTable(){
  var panel=new StackPanel();
  panel.Children.Add(Text(L("PreviewTitle"),18));
  panel.Children.Add(Text(F("Profiles",preview.Items.Count)+"   ·   "+F("Assignments",preview.Items.Sum(i=>i.Bindings))+"   ·   "+F("Axes",preview.Items.Sum(i=>i.Axes)),14,new SolidColorBrush(Color.FromRgb(88,106,128))));
  var grid=new Grid();
  for(int c=0;c<5;c++)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=c<2?new GridLength(1.4,GridUnitType.Star):new GridLength(1,GridUnitType.Star)});
  string[] headers={L("ColSource"),L("ColTarget"),L("Device"),L("ColBindings"),L("ColAxes")};
  for(int c=0;c<headers.Length;c++){var h=Text(headers[c],12,new SolidColorBrush(Color.FromRgb(90,100,115)));h.FontWeight=FontWeights.SemiBold;Grid.SetColumn(h,c);Grid.SetRow(h,0);grid.Children.Add(h);}
  grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  int row=1;
  foreach(var item in preview.Items){
   grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   string[] cells={item.SourceProfile,item.TargetProfile,string.IsNullOrEmpty(item.Device)?item.Category:item.Device+" · "+item.Category,item.Bindings.ToString(),item.Axes.ToString()};
   for(int c=0;c<cells.Length;c++){var cell=Text(cells[c],12);cell.Margin=new Thickness(0,2,8,2);Grid.SetColumn(cell,c);Grid.SetRow(cell,row);grid.Children.Add(cell);}
   row++;
  }
  panel.Children.Add(grid);
  var skipped=preview.Items.SelectMany(i=>i.Skipped.Select(s=>new{Item=i,Skip=s})).ToList();
  if(skipped.Count>0){
   panel.Children.Add(Text(L("SkippedList"),14,new SolidColorBrush(Color.FromRgb(120,80,60))));
   foreach(var rowItem in skipped.Take(40))panel.Children.Add(Text(F("SkippedItem",rowItem.Skip.Context,rowItem.Skip.Action,rowItem.Skip.Reason),11,new SolidColorBrush(Color.FromRgb(110,90,80))));
   if(skipped.Count>40)panel.Children.Add(Text("… +"+(skipped.Count-40),11));
  }
  if(preview.Notices.Count>0)panel.Children.Add(Text(L("Ambiguous"),13,new SolidColorBrush(Color.FromRgb(105,120,139))));
  content.Children.Add(Card(panel));
 }

 static void DrawSteamSelector(){
  content.Children.Add(Text(L("Action"),22));
  content.Children.Add(Text(L("SteamPrompt"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));
  var accounts=migration.ListSteamAccounts().Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
  var box=new ComboBox{Width=360,Height=32,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=accounts,Margin=new Thickness(0,8,0,8)};
  if(accounts.Count>0)box.SelectedIndex=0;
  content.Children.Add(box);
  status=Text("",14);content.Children.Add(status);
  content.Children.Add(Button(L("SteamApply"),()=>{
   var pick=box.SelectedItem as SteamAccount;
   if(pick==null){ShowStatus(L("SteamPrompt"),true);return;}
   selectedSteamAccount=pick.Id;Scan();
  },true));
  content.Children.Add(Button(L("Rescan"),()=>{selectedSteamAccount=null;Scan();}));
 }

 static void DrawCoach(){
  content.Children.Add(Text(L("CoachTitle"),22));
  string issue=preview!=null&&preview.Issues.Count>0?preview.Issues[0]:L("NoCompatible");
  content.Children.Add(Text(issue,15,new SolidColorBrush(Color.FromRgb(105,120,139))));
  content.Children.Add(Card(Text(L("CoachSteps"),15)));
  status=Text(L("NothingChanged"),14);content.Children.Add(status);
  content.Children.Add(Button(L("Rescan"),Scan,true));
  content.Children.Add(Button(L("Restore"),Restore));
  var backup=new StackPanel();backup.Children.Add(Text(L("BackupStored"),13));backup.Children.Add(Text(backupRoot,12,new SolidColorBrush(Color.FromRgb(105,120,139))));backup.Children.Add(Button(L("ChangeBackup"),ChooseBackupRoot));backup.Children.Add(Button(L("SaveReport"),SaveDiagnostic));
  content.Children.Add(new Expander{Header=L("BackupSection"),Content=backup,Margin=new Thickness(0,14,0,0)});
 }

 static void DrawFailure(string heading,string message){content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(heading,22));status=Text(message,15,new SolidColorBrush(Color.FromRgb(166,54,54)));content.Children.Add(status);content.Children.Add(Button(L("Rescan"),Scan,true));}
 static string[] SafeSessions(){try{return MigrationTransaction.Sessions(backupRoot);}catch{return new string[0];}}
 static string SafeState(string manifest){try{return MigrationTransaction.State(manifest);}catch{return "Invalid";}}
 static bool SafeLegacyRepair(string manifest){try{return MigrationTransaction.NeedsLegacyRepair(manifest);}catch{return false;}}
 static void ChangeLanguage(AppLanguage selected){if(selected==null||selected.Code==language.Code)return;language=selected;Directory.CreateDirectory(Path.GetDirectoryName(LanguagePreference));File.WriteAllText(LanguagePreference,language.Code);Draw();}
 static void ChooseBackupRoot(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L("BackupDescription"),SelectedPath=backupRoot})if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){backupRoot=dialog.SelectedPath;Directory.CreateDirectory(Path.GetDirectoryName(Preferences));File.WriteAllText(Preferences,backupRoot);migration=CreateMigration();Draw();}}
 static void SaveDiagnostic(){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"diagnostics.txt"),preview!=null&&preview.UnderlyingPlan!=null?AutoMigration.RedactedReport(preview.UnderlyingPlan):"No plan");ShowStatus(L("DiagnosticSaved"));}

 static async void ExportForImport(){
  window.IsEnabled=false;try{
   if(legacyRepair!=null){await RunLegacyRepairThenRefresh();if(preview==null||!preview.CanExport)return;}
   AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores);
   string folder=Library.DefaultRoot;
   using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L("ExportFolderDescription"),SelectedPath=folder})if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK)folder=dialog.SelectedPath;
   else{ShowStatus(L("WriteRequiresConfirm"),true);return;}
   var result=await Task.Run(()=>migration.ExportForImport(preview,folder));
   try{Process.Start(new ProcessStartInfo{FileName=result.Folder,UseShellExecute=true});}catch{}
   ShowStatus(L("ExportDone"));
   MessageBox.Show(window,F("ImportSteps",result.Folder),L("ExportTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static async void WriteDirect(){
  window.IsEnabled=false;try{
   if(preview==null||(!preview.CanWriteToGame&&legacyRepair==null))throw new IOException(L("NothingToExport"));
   if(legacyRepair!=null){await RunLegacyRepairThenRefresh();if(preview==null||!preview.CanWriteToGame)return;}
   string list=string.Join("\n",preview.Items.Select(i=>"• "+i.TargetProfile+" ("+i.Device+")"));
   string body=F("ConfirmWriteBody",list);
   var answer=MessageBox.Show(window,body,L("ConfirmWriteTitle"),MessageBoxButton.OKCancel,MessageBoxImage.Warning);
   if(answer!=MessageBoxResult.OK){ShowStatus(L("WriteRequiresConfirm"),true);return;}
   var confirmation=WriteConfirmation.FromUiDialog(body);
   string result=await Task.Run(()=>migration.WriteToGame(preview,confirmation));
   primaryExport.IsEnabled=false;ShowStatus(L("TransferDone"));
   MessageBox.Show(window,F("TransferMessage",Path.GetDirectoryName(result)),L("TransferTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static async Task RunLegacyRepairThenRefresh(){
  var current=AutoMigration.Discover();var document=MigrationTransaction.Verify(legacyRepair);var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))throw new IOException(L("WrongInstall"));
  await Task.Run(()=>MigrationTransaction.Restore(legacyRepair,()=>AutoMigration.RequireClosed(current)));
  legacyRepair=null;preview=migration.Prepare(selectedSteamAccount);
 }

 static async void Restore(){
  window.IsEnabled=false;try{
   string[] sessions=SafeSessions();string selected=sessions.FirstOrDefault(m=>SafeState(m)=="Pending"||SafeState(m)=="Restoring")??sessions.FirstOrDefault(m=>SafeState(m)=="Completed"&&SafeEffective(m));if(selected==null)throw new IOException(L("NoBackup"));
   var document=MigrationTransaction.Verify(selected);var current=AutoMigration.Discover();var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))throw new IOException(L("WrongInstall"));
   await Task.Run(()=>MigrationTransaction.Restore(selected,()=>AutoMigration.RequireClosed(current)));ShowStatus(L("Restored"));MessageBox.Show(window,L("Restored"),L("RestoreTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static bool SafeEffective(string manifest){try{return MigrationTransaction.HasEffectiveChanges(manifest);}catch{return false;}}

 static void RenderAndClose(){window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create("preview-auto.png"))png.Save(file);Application.Current.Shutdown();}
}
}
