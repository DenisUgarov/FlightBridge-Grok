using System;
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
 static Button transfer;
 static AutomaticPlan plan;
 static string legacyRepair;
 static AppLanguage language;
 static string backupRoot=MigrationTransaction.DefaultRoot;
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
 static Border Card(UIElement child){return new Border{Child=child,Background=Gradient(252,253,254,228,232,237),BorderBrush=new SolidColorBrush(Color.FromRgb(174,181,190)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=new Thickness(0,9,0,9),Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=0.16,Color=Colors.Black}};}
 static void ShowStatus(string message,bool error=false){if(status==null)return;status.Text=message;status.Foreground=new SolidColorBrush(error?Color.FromRgb(166,54,54):Color.FromRgb(64,91,74));}

 public static void Run(bool renderOnly,string languageOverride=null){
  render=renderOnly;
  string selected=null;try{if(File.Exists(LanguagePreference))selected=File.ReadAllText(LanguagePreference).Trim();else{string installed=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language.txt");if(File.Exists(installed))selected=File.ReadAllText(installed).Trim();}}catch{}
  language=AppLocalization.Resolve(!string.IsNullOrWhiteSpace(languageOverride)?languageOverride:string.IsNullOrWhiteSpace(selected)?System.Globalization.CultureInfo.CurrentUICulture.Name:selected);
  try{if(File.Exists(Preferences)){string saved=File.ReadAllText(Preferences).Trim();if(Path.IsPathRooted(saved))backupRoot=saved;}}catch{}
  var app=new Application();content=new StackPanel{Margin=new Thickness(44,28,44,32)};
  var shell=new Grid{Background=Gradient(239,241,244,198,204,211)};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  var titleGrid=new Grid();titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});
  var titleRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};titleRow.Children.Add(new Image{Source=AppIcon(),Width=28,Height=28,Margin=new Thickness(0,0,9,0)});titleRow.Children.Add(new TextBlock{Text="Flight Bridge",FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(45,52,62)),VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(titleRow,1);titleGrid.Children.Add(titleRow);
  var languageBox=new ComboBox{Width=155,Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,ItemsSource=AppLocalization.All,SelectedItem=language};languageBox.SelectionChanged+=(s,e)=>ChangeLanguage(languageBox.SelectedItem as AppLanguage);Grid.SetColumn(languageBox,2);titleGrid.Children.Add(languageBox);
  var title=new Border{Background=Gradient(237,239,242,183,189,198),BorderBrush=new SolidColorBrush(Color.FromRgb(132,139,149)),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(18,8,18,8),Child=titleGrid};Grid.SetRow(title,0);shell.Children.Add(title);
  var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);shell.Children.Add(scroll);
  var footer=new Border{Background=Gradient(211,216,222,188,194,202),BorderBrush=new SolidColorBrush(Color.FromRgb(150,157,166)),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(18,8,18,8),Child=new TextBlock{Text="© 2026 Denis Ugarov",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(80,88,98)),HorizontalAlignment=HorizontalAlignment.Center}};Grid.SetRow(footer,2);shell.Children.Add(footer);
  window=new Window{Title="Flight Bridge",Width=900,Height=760,MinWidth=680,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(218,222,227)),FontFamily=new FontFamily("Segoe UI"),Foreground=new SolidColorBrush(Color.FromRgb(32,44,59)),Content=shell};
  window.Loaded+=(s,e)=>Scan();app.Run(window);
 }

 static async void Scan(){
  window.IsEnabled=false;content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(L("Scanning"),18,new SolidColorBrush(Color.FromRgb(105,120,139))));
  try{var stores=await Task.Run(()=>AutoMigration.Discover());plan=AutoMigration.Build(stores);legacyRepair=SafeSessions().FirstOrDefault(m=>SafeLegacyRepair(m));Draw();}
  catch(Exception ex){DrawFailure(L("OperationFailed"),ErrorText(ex));}
  finally{window.IsEnabled=true;if(render)RenderAndClose();}
 }

 static void Draw(){
  bool repair=legacyRepair!=null;
  content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(plan.Ready||repair?L("Ready"):L("Action"),22));
  content.Children.Add(Text(repair?(language.Code=="ru"?"Найдена запись предыдущей версии. Flight Bridge сам вернёт исходный профиль и сразу выполнит исправленный перенос.":L("ReadyDescription")):plan.Ready?L("ReadyDescription"):(language.Code=="ru"?plan.Issues.FirstOrDefault():null)??L("NoCompatible"),15,new SolidColorBrush(Color.FromRgb(105,120,139))));
  if(plan.Ready){
   var summary=new StackPanel();summary.Children.Add(Text(F("Profiles",plan.Changes.Count),18));summary.Children.Add(Text(F("Assignments",plan.Changes.Sum(p=>p.Copied))+"   ·   "+F("Axes",plan.Changes.Sum(p=>p.Axes)),14,new SolidColorBrush(Color.FromRgb(88,106,128))));
   if(plan.Notices.Count>0)summary.Children.Add(Text(L("Ambiguous"),13,new SolidColorBrush(Color.FromRgb(105,120,139))));content.Children.Add(Card(summary));
  }
  status=Text(plan.Ready||repair?L("CloseApps"):L("NothingChanged"),14);content.Children.Add(status);
  var actions=new WrapPanel();transfer=Button(L("Transfer"),Transfer,true);transfer.IsEnabled=plan.Ready||repair;actions.Children.Add(transfer);actions.Children.Add(Button(L("Rescan"),Scan));content.Children.Add(actions);content.Children.Add(Button(L("Restore"),Restore));
  var details=new StackPanel();foreach(var store in plan.Stores)details.Children.Add(Text(F("GameProfiles",store.Year=="2020"?"MSFS 2020":"MSFS 2024",store.Edition=="Steam"?"Steam":"Microsoft Store",store.Profiles.Count),13));
  foreach(var change in plan.Changes){string result=F("ChangeResult",change.Source.Name,change.Target.Name,change.Copied,change.Axes);if(change.Skipped.Count>0)result+=", "+F("Unchanged",change.Skipped.Count);details.Children.Add(Text(result,13));}
  if(language.Code=="ru")foreach(var notice in plan.Notices)details.Children.Add(Text(notice,12,new SolidColorBrush(Color.FromRgb(105,120,139))));content.Children.Add(new Expander{Header=L("Details"),Content=details,Margin=new Thickness(0,14,0,4)});
  var backup=new StackPanel();backup.Children.Add(Text(L("BackupStored"),13));backup.Children.Add(Text(backupRoot,12,new SolidColorBrush(Color.FromRgb(105,120,139))));backup.Children.Add(Button(L("ChangeBackup"),ChooseBackupRoot));backup.Children.Add(Button(L("SaveReport"),SaveDiagnostic));
  content.Children.Add(new Expander{Header=L("BackupSection"),Content=backup,Margin=new Thickness(0,4,0,0)});
  string pending=SafeSessions().FirstOrDefault(m=>SafeState(m)=="Pending"||SafeState(m)=="Restoring");if(pending!=null){transfer.IsEnabled=false;ShowStatus(L("Interrupted"),true);}
 }

 static void DrawFailure(string heading,string message){content.Children.Clear();content.Children.Add(Text("Flight Bridge",30));content.Children.Add(Text(heading,22));status=Text(message,15,new SolidColorBrush(Color.FromRgb(166,54,54)));content.Children.Add(status);content.Children.Add(Button("Проверить снова",Scan,true));}
 static string[] SafeSessions(){try{return MigrationTransaction.Sessions(backupRoot);}catch{return new string[0];}}
 static string SafeState(string manifest){try{return MigrationTransaction.State(manifest);}catch{return "Invalid";}}
 static bool SafeLegacyRepair(string manifest){try{return MigrationTransaction.NeedsLegacyRepair(manifest);}catch{return false;}}
 static void ChangeLanguage(AppLanguage selected){if(selected==null||selected.Code==language.Code)return;language=selected;Directory.CreateDirectory(Path.GetDirectoryName(LanguagePreference));File.WriteAllText(LanguagePreference,language.Code);Draw();}
 static void ChooseBackupRoot(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L("BackupDescription"),SelectedPath=backupRoot})if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){backupRoot=dialog.SelectedPath;Directory.CreateDirectory(Path.GetDirectoryName(Preferences));File.WriteAllText(Preferences,backupRoot);Draw();}}
 static void SaveDiagnostic(){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"diagnostics.txt"),AutoMigration.RedactedReport(plan));ShowStatus(L("DiagnosticSaved"));}

 static async void Transfer(){
  window.IsEnabled=false;try{
   AutoMigration.RequireClosed(plan.Stores);
   if(legacyRepair!=null){var current=AutoMigration.Discover();var document=MigrationTransaction.Verify(legacyRepair);var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))throw new IOException(L("WrongInstall"));await Task.Run(()=>MigrationTransaction.Restore(legacyRepair,()=>AutoMigration.RequireClosed(current)));}
   var fresh=AutoMigration.Build(AutoMigration.Discover());if(!fresh.Ready)throw new IOException(L("StateChanged"));
   if(legacyRepair==null&&(fresh.Changes.Count!=plan.Changes.Count||fresh.Changes.Any(p=>!plan.Changes.Any(old=>old.Source.Path==p.Source.Path&&old.Target.Path==p.Target.Path&&Engine.Unchanged(old.Source)&&Engine.Unchanged(old.Target)))))throw new IOException(L("StateChanged"));
   var preview=Migration.FromPlan(fresh);string result=await Task.Run(()=>{var wr=Migration.WriteToGameResult(preview,WriteConfirmation.Confirm(preview),backupRoot);if(!wr.Success)throw new InvalidOperationException(wr.Error??L("OperationFailed"));return wr.BackupManifest;});transfer.IsEnabled=false;ShowStatus(L("TransferDone"));
   MessageBox.Show(window,F("TransferMessage",Path.GetDirectoryName(result)),L("TransferTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static async void Restore(){
  window.IsEnabled=false;try{
   string[] sessions=SafeSessions();string selected=sessions.FirstOrDefault(m=>SafeState(m)=="Pending"||SafeState(m)=="Restoring")??sessions.FirstOrDefault(m=>SafeState(m)=="Completed"&&SafeEffective(m));if(selected==null)throw new IOException(L("NoBackup"));
   var document=MigrationTransaction.Verify(selected);var current=AutoMigration.Discover();var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))throw new IOException(L("WrongInstall"));
   await Task.Run(()=>Migration.Restore(selected));ShowStatus(L("Restored"));MessageBox.Show(window,L("Restored"),L("RestoreTitle"),MessageBoxButton.OK,MessageBoxImage.Information);
  }catch(Exception ex){ShowStatus(ErrorText(ex),true);}finally{window.IsEnabled=true;}
 }

 static bool SafeEffective(string manifest){try{return MigrationTransaction.HasEffectiveChanges(manifest);}catch{return false;}}

 static void RenderAndClose(){window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create("preview-auto.png"))png.Save(file);Application.Current.Shutdown();}
}
}
