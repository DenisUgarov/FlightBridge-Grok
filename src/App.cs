using System;
using System.Linq;
namespace FSMigrator {
public static class App {
 [STAThread] public static void Main(string[] args){
  string language=args.FirstOrDefault(a=>a.StartsWith("--lang=",StringComparison.OrdinalIgnoreCase));
  if(language!=null)language=language.Substring(7);
  bool diagnostics=args.Any(a=>string.Equals(a,"--diagnostics",StringComparison.OrdinalIgnoreCase)||string.Equals(a,"--diagnose",StringComparison.OrdinalIgnoreCase));
  try{AutomaticApp.Run(args.Contains("--render"),language,diagnostics);}
  catch(Exception ex){var selected=AppLocalization.Resolve(language??System.Globalization.CultureInfo.CurrentUICulture.Name);System.Windows.MessageBox.Show(selected["OperationFailed"]+"\n\n"+ex.Message,"Flight Bridge");}
 }
}
}
