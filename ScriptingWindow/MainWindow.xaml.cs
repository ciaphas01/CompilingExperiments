using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ScriptingWindow
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ISymbol> IntellisenseSymbols { get; private set; }

        private Assembly _assembly = null;
        private AppDomain _appDomain = null;

        public MainWindow()
        {
            InitializeComponent();

            IntellisenseSymbols = new ObservableCollection<ISymbol>();

            DataContext = this;

            Logger.LogWritten += (s, msg) =>
                                 {
                                     txtOutput.Text += msg + "\n";
                                     txtOutputView.ScrollToEnd();
                                 };
        }

        private List<Record> _Records;

        [Serializable]
        public class UserCodeExecutionContext
        {
            public Record CurrentRecord;
            public void DoAThing()
            {
                Logger.Write("Doing a thing");
            }
        }

        [Serializable]
        public class AssemblyWorker : MarshalByRefObject
        {
            private static Assembly _assembly = null;
            public static Assembly Assembly => _assembly;
            public static void Load()
            {
                byte[] dllBytes = AppDomain.CurrentDomain.GetData("dllBytes") as byte[];
                byte[] pdbBytes = AppDomain.CurrentDomain.GetData("pdbBytes") as byte[];
                _assembly = AppDomain.CurrentDomain.Load(dllBytes, pdbBytes);
            }
            
            public static void PrintDomainStatic()
            {
                Logger.Write(AppDomain.CurrentDomain.FriendlyName);
            }

            public static void Execute()
            {
                if (_assembly == null) return;

                UserCodeExecutionContext executionContext = AppDomain.CurrentDomain.GetData("executionContext") as UserCodeExecutionContext;
                var type = _assembly.GetType("UserCodeClass");
                var method = type.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public);
                method.Invoke(null, new object[] { new object[2] { executionContext, null } });
                AppDomain.CurrentDomain.SetData("executionContext", executionContext);
            }
        }

        private void CreateRecords()
        {
            _Records = new List<Record>();
            Record r = new Record();
            r.RecordTime = DateTime.Now;
            Row row = new Row();
            row["foo"] = 1;
            row["bar"] = 2;
            r.Rows.Add(row);
            _Records.Add(r);

            r = new Record();
            r.RecordTime = DateTime.Now;
            row = new Row();
            row["foo"] = 3;
            row["bar"] = 4;
            r.Rows.Add(row);
            _Records.Add(r);
        }

        private Script Setup()
        {
            CreateRecords();

            string script = $"#line 1 \"./UserCode.cs\"\r\n{txtCode.Text}";
            //string script = txtCode.Text;

            return CSharpScript.Create(script, ScriptOptions.Default
                                                            .WithReferences(new MetadataReference[] {
                                                                                                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                                                                                                        MetadataReference.CreateFromFile (typeof(Logger).Assembly.Location) // for Logger class
                                                                                                    })
                                                            .WithImports(
                                                                            "ScriptingWindow" // for static Logger class
                                                                            , "System.Collections.Generic" // for IEnumerable goodies
                                                            ).WithEmitDebugInformation(true),
                                          typeof(UserCodeExecutionContext)
                                      );
        }
        private void cmdGo_Click(object sender, RoutedEventArgs e)
        {
            Script script = Setup();
            var comp = script.GetCompilation();
            comp = comp.WithOptions(comp.Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                                                .WithPlatform(Platform.X64)
                                                .WithOptimizationLevel(OptimizationLevel.Debug)
                                                .WithModuleName("UserCode")
                                                .WithScriptClassName("UserCodeClass")
                                   ).WithAssemblyName("UserCode");

            if (_assembly != null)
            {
                _assembly = null;
                AppDomain.Unload(_appDomain);
                _appDomain = null;

                // are these 3 lines needed??? test thoroughly!
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                // force-kill vsdbg-ui.exe? or tell the user to Detach Or Else?
            }

            using (var modFS = new FileStream("UserCode.dll", FileMode.Create))
            using (var pdbFS = new FileStream("UserCode.pdb", FileMode.Create))
            {
                EmitResult result = comp.Emit(modFS, pdbFS, options: new EmitOptions(false, DebugInformationFormat.PortablePdb));
                if (result.Success)
                {
                    Logger.Write("Compiled and saved!");
                }
                else
                {
                    Logger.Write("Compile failed! Diagnostics follow");
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                                                                                           diagnostic.IsWarningAsError ||
                                                                                           diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        Logger.Write($"{diagnostic.Id} - {diagnostic.Location}: {diagnostic.GetMessage()}");
                    }

                    return;
                }
            }
            
            var appDomainSetup = new AppDomainSetup();
            appDomainSetup.ShadowCopyFiles = "true";
            appDomainSetup.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
            appDomainSetup.ApplicationName = "UserCode.dll";
            appDomainSetup.LoaderOptimization = LoaderOptimization.SingleDomain;
            _appDomain = AppDomain.CreateDomain("UserCodeDomain", AppDomain.CurrentDomain.Evidence, appDomainSetup);
            byte[] dllBytes = File.ReadAllBytes("UserCode.dll");
            byte[] pdbBytes = File.ReadAllBytes("UserCode.pdb");

            //_assembly = Assembly.Load(dllBytes, pdbBytes);
            //_assembly = _appDomain.Load(dllBytes, pdbBytes);
            _appDomain.SetData("dllBytes", dllBytes);
            _appDomain.SetData("pdbBytes", pdbBytes);
            _appDomain.DoCallBack(AssemblyWorker.Load);

            UserCodeExecutionContext executionContext = new UserCodeExecutionContext();
            executionContext.CurrentRecord = _Records[0];

            Logger.Write($"Current record time = {executionContext.CurrentRecord.RecordTime}");
            _appDomain.SetData("executionContext", executionContext);
            _appDomain.DoCallBack(AssemblyWorker.Execute);
            executionContext = _appDomain.GetData("executionContext") as UserCodeExecutionContext;
            Logger.Write($"Post-invoke record time = {executionContext.CurrentRecord.RecordTime}");

            return;

            var assemblies = _appDomain.GetAssemblies();
            _assembly = _appDomain.GetAssemblies().First((x) => x.GetName().Name == "UserCode");

            
            var type = _assembly.GetType("UserCodeClass");
            var method = type.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public);
            
            Logger.Write($"Current record time = {executionContext.CurrentRecord.RecordTime}");
            method.Invoke(null, new object[] { new object[2] { executionContext, null } });
            Logger.Write($"Post-invoke record time = {executionContext.CurrentRecord.RecordTime}");

            AppDomain.Unload(_appDomain);
            _assembly = null;
            _appDomain = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            /*
            try
            {
                ScriptGlobals g = new ScriptGlobals();
                foreach (var currentRecord in _Records)
                {
                    g.CurrentRecord = currentRecord;
                    ScriptState state = script.RunAsync(g).Result;
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"Exception: {ex.Message}");
            }
            */
        }

        private void cmdAnalyze_Click(object sender, RoutedEventArgs e)
        {
            Script script = Setup();

            Compilation compilation = script.GetCompilation();
            SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
            SyntaxNode syntaxTreeRoot = syntaxTree.GetRoot();
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

            var variableDecls = syntaxTreeRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>();

            foreach (var variableDecl in variableDecls)
            {
                // see if variableDecl contains an InvocationExpression
                var invocation = variableDecl.DescendantNodes().OfType<InvocationExpressionSyntax>().SingleOrDefault();

                // and if it does, and it's calling ValueReference, we can use this Invocation
                if (invocation?.ChildNodes().OfType<IdentifierNameSyntax>().SingleOrDefault()?.Identifier.Text == "ValueReference")
                {
                    var invSym = semanticModel.GetSymbolInfo(invocation);

                    var arguments = invocation.DescendantNodes().OfType<ArgumentSyntax>();
                    int argcnt = -1;
                    foreach (var argument in arguments)
                    {
                        ++argcnt;
                        Logger.Write($"Analyzing arg {argcnt}");
                        var argIdentity = argument.ChildNodes().First();
                        if (argIdentity is IdentifierNameSyntax)
                        {
                            Logger.Write("Identifer found, tracing back to decl if possible");
                            var declaringNode = semanticModel.GetSymbolInfo(argIdentity).Symbol.DeclaringSyntaxReferences.First().GetSyntax();
                            string origVal = declaringNode.DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Token.Value as string;
                            if (origVal != null) Logger.Write($"Original declared value: {origVal}");
                        }
                        else if (argIdentity is LiteralExpressionSyntax)
                        {
                            Logger.Write($"Literal arg value is {((LiteralExpressionSyntax)argIdentity).Token.Value as string}");
                        }
                    }
                }
            }
        }
        
        private void ShowIntellisensePopup(Rect pos)
        {
            intellisensePopup.PlacementTarget = txtCode;
            intellisensePopup.PlacementRectangle = pos;
            intellisensePopup.IsOpen = true;
        }

        private void txtCode_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            /*
            intellisensePopup.IsOpen = false;
            if (e.Text == ".")
            {
                // get the script up to now--not compilable but can still get syntax tree
                Script script = Setup();
                Compilation compilation = script.GetCompilation();
                SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
                SyntaxNode syntaxTreeRoot = syntaxTree.GetRoot();
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

                // let's try searching backwards from the last available period token and go from there
                // TODO work from the caret position instead

                SyntaxToken lastDot = syntaxTreeRoot.GetLastToken();
                // TODO make sure the above is a dot
                //

                var identifier = lastDot.Parent as IdentifierNameSyntax;
                var lhsType = semanticModel.GetTypeInfo(identifier).Type;

                var symbols = semanticModel.LookupSymbols(txtCode.CaretIndex, lhsType);

                IntellisenseSymbols.Clear();
                foreach (var symbol in symbols.GroupBy(x => x.Name).Select(grp => grp.First()))
                {
                    IntellisenseSymbols.Add(symbol);
                }

                // finally, show the popup
                ShowIntellisensePopup(txtCode.GetRectFromCharacterIndex(txtCode.CaretIndex, true));
            }
            else if (e.Text == "(")
            {
                Script script = Setup();
                Compilation compilation = script.GetCompilation();
                SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();

                SyntaxNode syntaxTreeRoot = syntaxTree.GetRoot();
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

                var identifier = syntaxTreeRoot.FindToken(txtCode.CaretIndex - 1).Parent;
                var methodSymbolCandidates = semanticModel.GetSymbolInfo(identifier);

                // for now just take the first candidate if multiple
                var methodSymbol = methodSymbolCandidates.CandidateSymbols.FirstOrDefault();

                int xxx = 0;
            }
            */
        }
    }
}
