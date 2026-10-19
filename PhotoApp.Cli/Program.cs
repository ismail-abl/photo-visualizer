using System.CommandLine;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var root = new RootCommand("PhotoManager en ligne de commande.");
return root.Parse(args).Invoke();
