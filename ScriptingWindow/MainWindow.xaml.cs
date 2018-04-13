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

            return CSharpScript.Create(txtCode.Text, ScriptOptions.Default
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
        }

        private void cmdAnalyze_Click(object sender, RoutedEventArgs e)
        {
            Script script = Setup();

            Compilation compilation = script.GetCompilation();
            SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
            syntaxTree = syntaxTree.WithRootAndOptions(syntaxTree.GetRoot(), syntaxTree.Options.WithDocumentationMode(DocumentationMode.Parse));
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
            intellisensePopup.IsOpen = false;
            if (e.Text == ".")
            {
                // get the script up to now--not compilable but can still get syntax tree
                Script script = Setup();
                Compilation compilation = script.GetCompilation();
                SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
                syntaxTree = syntaxTree.WithRootAndOptions(syntaxTree.GetRoot(), syntaxTree.Options.WithDocumentationMode(DocumentationMode.Parse));
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
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(txtCode.Text, CSharpParseOptions.Default
                                                                   .WithDocumentationMode(DocumentationMode.Parse)
                                                                   .WithKind(SourceCodeKind.Script)
                                                                   );
                
                //XmlReferenceResolver xmlReferenceResolver = new X

                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default);

                var compilation = CSharpCompilation.Create("parenAssem")
                    .AddSyntaxTrees(syntaxTree)
                    .AddReferences(new MetadataReference[] {
                                   MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                                   MetadataReference.CreateFromFile (typeof(Logger).Assembly.Location) // for Logger class
                    })
                    .WithOptions(options);

                //Script script = Setup();
                //Compilation compilation = script.GetCompilation();
                //SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
                //syntaxTree = syntaxTree.WithRootAndOptions(syntaxTree.GetRoot(), syntaxTree.Options.WithDocumentationMode(DocumentationMode.Parse));
                SyntaxNode syntaxTreeRoot = syntaxTree.GetRoot();
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

                var identifier = syntaxTreeRoot.FindToken(txtCode.CaretIndex - 1).Parent;
                var methodSymbolCandidates = semanticModel.GetSymbolInfo(identifier);

                // for now just take the first candidate if multiple
                var methodSymbol = methodSymbolCandidates.CandidateSymbols.FirstOrDefault();
                string xmlDocument = methodSymbol?.GetDocumentationCommentXml();

                Logger.Write(xmlDocument);
            }
        }
    }
}
