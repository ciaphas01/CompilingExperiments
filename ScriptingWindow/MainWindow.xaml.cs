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
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Completion;
using System.Collections.Immutable;

namespace ScriptingWindow
{
    public partial class MainWindow : Window
    {
        private ImmutableArray<CompletionItem> AllIntellisenseItems;
        public ObservableCollection<string> FilteredIntellisenseItems { get; private set; }

        private readonly AdhocWorkspace workspace;
        private Document document;

        public MainWindow()
        {
            InitializeComponent();

            FilteredIntellisenseItems = new ObservableCollection<string>();

            DataContext = this;

            Logger.LogWritten += (s, msg) =>
                                 {
                                     txtOutput.Text += msg + "\n";
                                     txtOutputView.ScrollToEnd();
                                 };

            this.workspace = new AdhocWorkspace();
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default));
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp,
                metadataReferences: new MetadataReference[] {
                            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                            MetadataReference.CreateFromFile (typeof(Logger).Assembly.Location) // for Logger class
                        }));
            this.document = workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), "Script.csx", sourceCodeKind: SourceCodeKind.Script));
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

        private async void txtCode_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

            var completionService = CompletionService.GetService(document);
            var sourceText = SourceText.From(txtCode.Text);

            if (txtCode.Text.Length > 0 &&
                completionService.ShouldTriggerCompletion(
                    SourceText.From(txtCode.Text),
                    txtCode.CaretIndex,
                    CompletionTrigger.CreateInsertionTrigger(txtCode.Text[txtCode.CaretIndex - 1])))
            {
                var newSolution = workspace.CurrentSolution.WithDocumentText(document.Id, sourceText);
                workspace.TryApplyChanges(newSolution);
                document = newSolution.GetDocument(document.Id);

                var completionList = await completionService.GetCompletionsAsync(document, txtCode.CaretIndex);
                if (completionList != null)
                {
                    AllIntellisenseItems = completionList.Items;

                    // finally, show the popup
                    ShowIntellisensePopup(txtCode.GetRectFromCharacterIndex(txtCode.CaretIndex, true));
                }
            }

            if (intellisensePopup.IsOpen)
            {
                var span = completionService.GetDefaultCompletionListSpan(sourceText, txtCode.CaretIndex);
                var filterText = sourceText.GetSubText(span).ToString();

                FilteredIntellisenseItems.Clear();
                foreach (var item in AllIntellisenseItems)
                {
                    if (item.DisplayText.StartsWith(filterText, StringComparison.InvariantCultureIgnoreCase))
                        FilteredIntellisenseItems.Add(item.DisplayText);
                }
            }
        }
    }
}
