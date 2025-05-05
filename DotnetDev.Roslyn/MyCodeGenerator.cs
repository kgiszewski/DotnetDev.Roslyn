using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DotnetDev.Roslyn;

public interface ICodeGenerator
{
    string InterpolateWithModel<T>(string templateName, T model, IReadOnlyCollection<string> namespaces = null) where T : class;
}

public class MyCodeGenerator : ICodeGenerator
{
    private static readonly ConcurrentDictionary<string, Assembly> AssemblyCache = new ConcurrentDictionary<string, Assembly>();
    private const string MethodName = "Interpolate";
    private static readonly IEnumerable<string> DefaultNamespaces =
    [
        "System"
    ];
    
    private static readonly IEnumerable<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(Assembly.GetAssembly(typeof(String))!.Location),
        MetadataReference.CreateFromFile(Assembly.GetAssembly(typeof(System.Linq.Enumerable)).Location),
        MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location)
    ];
        
    private static readonly CSharpCompilationOptions DefaultCompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithOverflowChecks(true)
        .WithOptimizationLevel(OptimizationLevel.Release)
        .WithUsings(DefaultNamespaces);
    
    private static string generateAssemblyName(string name)
    {
        return $"MyStringTemplate_{name.Replace("/", "_").Replace(".", "_")}";
    }  
    
    private static string generateClassNameFromNamespace(string namespaceName)
    {
        return $"{namespaceName}TemplateGenerator";
    }
    
    private Assembly createAssembly(string templateName, string fileContents, Type modelType, IReadOnlyCollection<string> namespaces = null)
    {
        if (modelType?.FullName == null)
        {
            throw new Exception("ModelType is null");
        }

        //generate the code
        var codeBuilder = new StringBuilder();
        var namespaceName = generateAssemblyName(templateName);
        var className = generateClassNameFromNamespace(namespaceName);

        //massage the input
        fileContents = fileContents
            .Replace(Environment.NewLine, string.Empty)
            .Replace("\"", "\\\"")//normal quote
            .Replace("\\\"\\\"", "\"");//code quote
        
        if (namespaces != null)
        {
            foreach (var ns in namespaces)
            {
                codeBuilder.AppendLine($"using {ns};");
            }
        }
        
        codeBuilder.AppendLine($"namespace {namespaceName}");
        codeBuilder.AppendLine("{");
        codeBuilder.AppendLine($"    public class {className}");
        codeBuilder.AppendLine("     {");
        codeBuilder.AppendLine($"         public string {MethodName}({modelType.FullName.Replace("+", ".")} model)");
        codeBuilder.AppendLine("          {");
        codeBuilder.AppendLine($"             return $\"{fileContents}\";");
        codeBuilder.AppendLine("           }");
        codeBuilder.AppendLine("     }");
        codeBuilder.AppendLine("}");

        //compile the code with Rosyln
        var parsedSyntaxTree = getSyntaxTree(codeBuilder.ToString(), "", CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13));
        var compilation = CSharpCompilation.Create($"{templateName}Template", [parsedSyntaxTree], DefaultReferences, DefaultCompilationOptions);

        using (var stream = new MemoryStream())
        {
            //attempt to generate the byte code
            var emitResult = compilation.Emit(stream);

            //evaluate the compilation result
            if (emitResult.Success)
            {
                stream.Seek(0, SeekOrigin.Begin);

                var assembly = Assembly.Load(stream.ToArray());

                return assembly;
            }
            
            throw new Exception("Compilation failed");
        }
    }
    
    private string executeCode(Assembly assembly, string namespaceName, params object[] args)
    {
        var className = generateClassNameFromNamespace(namespaceName);
        var fullyQualifiedName = $"{namespaceName}.{className}";
        var instance = assembly.CreateInstance(fullyQualifiedName);

        if (instance == null)
        {
            throw new Exception($"Cannot create an instance of the object => {fullyQualifiedName}");
        }

        var type = instance.GetType();
        var method = type.GetMethod(MethodName);

        if (method == null)
        {
            throw new Exception($"Cannot find method {MethodName}");
        }
     
        return method.Invoke(instance, args).ToString();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="code"></param>
    /// <param name="filename"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    private static SyntaxTree getSyntaxTree(string code, string filename = "", CSharpParseOptions options = null)
    {
        var stringText = SourceText.From(code, Encoding.UTF8);
            
        return SyntaxFactory.ParseSyntaxTree(stringText, options, filename);
    }

    private static string getTemplateFileContents(string templateName)
    {
        //this string would come from a file, a db call or other trusted source
        return "simulated template string with named placeholders name => {model.Name} age => {model.Age}";
    }

    public string InterpolateWithModel<T>(string templateName, T model, IReadOnlyCollection<string> namespaces = null) where T : class
    {
        Assembly assembly;

        var modelType = typeof(T);

        //if Assembly not in cache, generate
        if (!AssemblyCache.ContainsKey(templateName))
        {
            //load the file from somewhere
            var fileContents = getTemplateFileContents(templateName);

            if (fileContents == null)
            {
                throw new Exception("Cannot find the resource");
            }

            assembly = createAssembly(templateName, fileContents, modelType, namespaces);

            AssemblyCache.TryAdd(templateName, assembly);
        }

        //if so fetch and interpolate
        assembly = AssemblyCache[templateName];

        var namespaceName = generateAssemblyName(templateName);
        var result = executeCode(assembly, namespaceName, model);

        return result;
    }
}