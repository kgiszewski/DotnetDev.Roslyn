//works fine//works fine

using DotnetDev.Roslyn;

var bar = 123;

Console.WriteLine($"Let's interpolate...{bar}"); //Let's interpolate...123 
Console.WriteLine($"{getExternally()}"); //"Let's interpolate...{bar}", doesn't do the interpolation

//REGEX, nah

//what about string.Format?
//Console.WriteLine(string.Format(getExternally(), 123)); //won't work as these are positional and only works if a compile-time const

//let's compile a template in real-time
var codeGenerator = new MyCodeGenerator();

var result = codeGenerator.InterpolateWithModel("someTemplateName", new MyModel
{
    Name = "Fred",
    Age = 35
});

Console.WriteLine(result);

Console.ReadKey();

string getExternally()
{
    //data we get from external can't be interpolated, it has to be a compile-time const
    return "Let's interpolate...{bar}";
}
