using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Interfaces;

namespace CompilingExperiments
{
    public class Program
    {
        static void Main(string[] args)
        {
            VBSample();

            Console.Write("--press any key to exit--");
            Console.ReadKey();
        }
        
        public class VBVisitor : VisualBasicSyntaxWalker
        {
            static int Tabs = -1;
            public override void Visit(SyntaxNode node)
            {
                Tabs++;
                var indents = new String(' ', Tabs * 2);
                //Console.WriteLine(indents + (node as VisualBasicSyntaxNode).Kind() + "   " + node.ToString());
                Console.WriteLine(indents + (node as VisualBasicSyntaxNode).Kind());
                base.Visit(node);
                Tabs--;
            }
        }

        public class VBUserCodeInjector : VisualBasicSyntaxRewriter
        {
            private SyntaxTree m_userCode;
            public VBUserCodeInjector(SyntaxTree userCode)
            {
                m_userCode = userCode;
            }

            public override SyntaxNode VisitMethodBlock(MethodBlockSyntax node)
            {
                if (node.SubOrFunctionStatement.Identifier.ToString() == "_Execute")
                {
                    var statements = m_userCode.GetRoot().ChildNodes();//.OfType<StatementSyntax>();
                    SyntaxList<SyntaxNode> syntaxList = new SyntaxList<SyntaxNode>(statements);
                    return base.VisitMethodBlock(node.WithStatements(syntaxList));
                }
                else
                    return base.VisitMethodBlock(node);
            }

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                // the script compile treats Dims at the top level as FieldDeclarations which doesn't make sense in context, so just
                // globally swap them to LocalDeclarations
                return Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory.LocalDeclarationStatement(node.Modifiers, node.Declarators);
            }
        }

        static void VBSample()
        {
            SyntaxTree hostTree = VisualBasicSyntaxTree.ParseText(@"
Namespace VBUserCode
    Class VBUserCodeContext
        Inherits DRTInterfaces.VBUserCodeContextBase
        Public Sub New(record As DRTInterfaces.IRecord)
            MyBase.New(record)
        End Sub
        Sub _Execute()
            
        End Sub
    End Class
End Namespace");
            
            string usercode = @"
For Each i In currentRecord.Rows
    Dim val = ValueReference(i, ""myValue"")
    val.Value = 4
    'val = 4
    System.Console.WriteLine(""Did a thing"")
Next
System.Console.WriteLine(""out of loop"")
Dim foo As Integer = 4
";
            SyntaxTree userCodeTree = VisualBasicSyntaxTree.ParseText(
                                     usercode, new VisualBasicParseOptions(kind: SourceCodeKind.Script)
                                 );

            var injectedSyntaxNode = new VBUserCodeInjector(userCodeTree).Visit(hostTree.GetRoot());

            MetadataReference[] references = new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.VisualBasic.CompilerServices.NewLateBinding).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(VBUserCodeContextBase).Assembly.Location)
            };

            string assemblyName = Path.GetRandomFileName();
            VisualBasicCompilation compilation = VisualBasicCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { injectedSyntaxNode.SyntaxTree },
                references: references,
                options: new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // new VBVisitor().Visit(injectedSyntaxNode);

            Assembly assembly = null;
            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        Console.Error.WriteLine("{0} - {1}: {2}", diagnostic.Id, diagnostic.Location, diagnostic.GetMessage());
                    }
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assembly = Assembly.Load(ms.ToArray());
                }
            }

            if (assembly != null)
            {
                var rec = new RecordStub();

                Type type = assembly.GetType("VBUserCode.VBUserCodeContext");
                var obj = Activator.CreateInstance(type, new object[] { rec }) as VBUserCodeContextBase;
                type.InvokeMember("_Execute",
                    BindingFlags.Default | BindingFlags.InvokeMethod,
                    null,
                    obj,
                    null);
            }
        }
    }
}
