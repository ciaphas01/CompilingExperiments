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

namespace ScriptingWindow
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ISymbol> IntellisenseSymbols { get; private set; }

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

        public class ScriptGlobals
        {
            public Record CurrentRecord;
            public void DoAThing()
            {
                Logger.Write("Doing a thing");
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
                        ).WithEmitDebugInformation(true)
                        , typeof(ScriptGlobals));
        }
        private void cmdGo_Click(object sender, RoutedEventArgs e)
        {
            Script script = Setup();
            var comp = script.GetCompilation();
            comp = comp.WithOptions(comp.Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                                                .WithPlatform(Platform.X64)
                                                .WithOptimizationLevel(OptimizationLevel.Debug)
                                                .WithModuleName("UserCode")
                                                );

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
                    Logger.Write("Failed!");
                }
            }

            var assembly = Assembly.Load(File.ReadAllBytes("UserCode.dll"), File.ReadAllBytes("UserCode.pdb"));

            var type = assembly.GetType("Submission#0");
            var method = type.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public);
            ScriptGlobals g = new ScriptGlobals();
            g.CurrentRecord = _Records[0];
            var parameters = method.GetParameters();
            method.Invoke(null, new object[] { new object[2] { g, null } });

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
