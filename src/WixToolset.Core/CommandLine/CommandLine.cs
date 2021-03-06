﻿// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using WixToolset.Data;
    using WixToolset.Extensibility;

    internal enum Commands
    {
        Unknown,
        Build,
        Preprocess,
        Compile,
        Link,
        Bind,
    }

    public class CommandLine
    {
        private CommandLine()
        {
        }

        public static string ExpectedArgument { get; } = "expected argument";

        public string ActiveCommand { get; private set; }

        public string[] OriginalArguments { get; private set; }

        public Queue<string> RemainingArguments { get; } = new Queue<string>();

        public ExtensionManager ExtensionManager { get; } = new ExtensionManager();

        public string ErrorArgument { get; set; }

        public bool ShowHelp { get; set; }

        public static ICommand ParseStandardCommandLine(string commandLineString)
        {
            var args = CommandLine.ParseArgumentsToArray(commandLineString).ToArray();

            return ParseStandardCommandLine(args);
        }

        public static ICommand ParseStandardCommandLine(string[] args)
        {
            var next = String.Empty;

            var command = Commands.Unknown;
            var showLogo = true;
            var showVersion = false;
            var outputFolder = String.Empty;
            var outputFile = String.Empty;
            var outputType = String.Empty;
            var verbose = false;
            var files = new List<string>();
            var defines = new List<string>();
            var includePaths = new List<string>();
            var locFiles = new List<string>();
            var libraryFiles = new List<string>();
            var suppressedWarnings = new List<int>();

            var bindFiles = false;
            var bindPaths = new List<string>();

            var intermediateFolder = String.Empty;

            var cultures = new List<string>();
            var contentsFile = String.Empty;
            var outputsFile = String.Empty;
            var builtOutputsFile = String.Empty;
            var wixProjectFile = String.Empty;

            var cli = CommandLine.Parse(args, (cmdline, arg) => Enum.TryParse(arg, true, out command), (cmdline, arg) =>
            {
                if (cmdline.IsSwitch(arg))
                {
                    var parameter = arg.TrimStart(new[] { '-', '/' });
                    switch (parameter.ToLowerInvariant())
                    {
                        case "?":
                        case "h":
                        case "help":
                            cmdline.ShowHelp = true;
                            return true;

                        case "bindfiles":
                            bindFiles = true;
                            return true;

                        case "bindpath":
                            cmdline.GetNextArgumentOrError(bindPaths);
                            return true;

                        case "cultures":
                            cmdline.GetNextArgumentOrError(cultures);
                            return true;
                        case "contentsfile":
                            cmdline.GetNextArgumentOrError(ref contentsFile);
                            return true;
                        case "outputsfile":
                            cmdline.GetNextArgumentOrError(ref outputsFile);
                            return true;
                        case "builtoutputsfile":
                            cmdline.GetNextArgumentOrError(ref builtOutputsFile);
                            return true;
                        case "wixprojectfile":
                            cmdline.GetNextArgumentOrError(ref wixProjectFile);
                            return true;

                        case "d":
                        case "define":
                            cmdline.GetNextArgumentOrError(defines);
                            return true;

                        case "i":
                        case "includepath":
                            cmdline.GetNextArgumentOrError(includePaths);
                            return true;

                        case "intermediatefolder":
                            cmdline.GetNextArgumentOrError(ref intermediateFolder);
                            return true;

                        case "loc":
                            cmdline.GetNextArgumentAsFilePathOrError(locFiles, "localization files");
                            return true;

                        case "lib":
                            cmdline.GetNextArgumentAsFilePathOrError(libraryFiles, "library files");
                            return true;

                        case "o":
                        case "out":
                            cmdline.GetNextArgumentOrError(ref outputFile);
                            return true;

                        case "outputtype":
                            cmdline.GetNextArgumentOrError(ref outputType);
                            return true;

                        case "nologo":
                            showLogo = false;
                            return true;

                        case "v":
                        case "verbose":
                            verbose = true;
                            return true;

                        case "version":
                        case "-version":
                            showVersion = true;
                            return true;
                    }

                    return false;
                }
                else
                {
                    files.AddRange(cmdline.GetFiles(arg, "source code"));
                    return true;
                }
            });

            Messaging.Instance.ShowVerboseMessages = verbose;

            if (showVersion)
            {
                return new VersionCommand();
            }

            if (showLogo)
            {
                AppCommon.DisplayToolHeader();
            }

            if (cli.ShowHelp)
            {
                return new HelpCommand(command);
            }

            switch (command)
            {
                case Commands.Build:
                    {
                        var sourceFiles = GatherSourceFiles(files, outputFolder);
                        var variables = GatherPreprocessorVariables(defines);
                        var bindPathList = GatherBindPaths(bindPaths);
                        var extensions = cli.ExtensionManager;
                        var type = CalculateOutputType(outputType, outputFile);
                        return new BuildCommand(extensions, sourceFiles, variables, locFiles, libraryFiles, outputFile, type, cultures, bindFiles, bindPathList, intermediateFolder, contentsFile, outputsFile, builtOutputsFile, wixProjectFile);
                    }

                case Commands.Compile:
                    {
                        var sourceFiles = GatherSourceFiles(files, outputFolder);
                        var variables = GatherPreprocessorVariables(defines);
                        return new CompileCommand(sourceFiles, variables);
                    }
            }

            return null;
        }

        private static OutputType CalculateOutputType(string outputType, string outputFile)
        {
            if (String.IsNullOrEmpty(outputType))
            {
                outputType = Path.GetExtension(outputFile);
            }

            switch (outputType.ToLowerInvariant())
            {
                case "bundle":
                case ".exe":
                    return OutputType.Bundle;

                case "library":
                case ".wixlib":
                    return OutputType.Library;

                case "module":
                case ".msm":
                    return OutputType.Module;

                case "patch":
                case ".msp":
                    return OutputType.Patch;

                case ".pcp":
                    return OutputType.PatchCreation;

                case "product":
                case ".msi":
                    return OutputType.Product;

                case "transform":
                case ".mst":
                    return OutputType.Transform;
            }

            return OutputType.Unknown;
        }

        private static CommandLine Parse(string commandLineString, Func<CommandLine, string, bool> parseArgument)
        {
            var arguments = CommandLine.ParseArgumentsToArray(commandLineString).ToArray();

            return CommandLine.Parse(arguments, null, parseArgument);
        }

        private static CommandLine Parse(string[] commandLineArguments, Func<CommandLine, string, bool> parseArgument)
        {
            return CommandLine.Parse(commandLineArguments, null, parseArgument);
        }

        private static CommandLine Parse(string[] commandLineArguments, Func<CommandLine, string, bool> parseCommand, Func<CommandLine, string, bool> parseArgument)
        {
            var cmdline = new CommandLine();

            cmdline.FlattenArgumentsWithResponseFilesIntoOriginalArguments(commandLineArguments);

            cmdline.QueueArgumentsAndLoadExtensions(cmdline.OriginalArguments);

            cmdline.ProcessRemainingArguments(parseArgument, parseCommand);

            return cmdline;
        }

        private static IEnumerable<SourceFile> GatherSourceFiles(IEnumerable<string> sourceFiles, string intermediateDirectory)
        {
            var files = new List<SourceFile>();

            foreach (var item in sourceFiles)
            {
                var sourcePath = item;
                var outputPath = Path.Combine(intermediateDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".wir");

                files.Add(new SourceFile(sourcePath, outputPath));
            }

            return files;
        }

        private static IDictionary<string, string> GatherPreprocessorVariables(IEnumerable<string> defineConstants)
        {
            var variables = new Dictionary<string, string>();

            foreach (var pair in defineConstants)
            {
                string[] value = pair.Split(new[] { '=' }, 2);

                if (variables.ContainsKey(value[0]))
                {
                    Messaging.Instance.OnMessage(WixErrors.DuplicateVariableDefinition(value[0], (1 == value.Length) ? String.Empty : value[1], variables[value[0]]));
                    continue;
                }

                variables.Add(value[0], (1 == value.Length) ? String.Empty : value[1]);
            }

            return variables;
        }

        private static IEnumerable<BindPath> GatherBindPaths(IEnumerable<string> bindPaths)
        {
            var result = new List<BindPath>();

            foreach (var bindPath in bindPaths)
            {
                BindPath bp = BindPath.Parse(bindPath);

                if (Directory.Exists(bp.Path))
                {
                    result.Add(bp);
                }
                else if (File.Exists(bp.Path))
                {
                    Messaging.Instance.OnMessage(WixErrors.ExpectedDirectoryGotFile("-bindpath", bp.Path));
                }
            }

            return result;
        }

        /// <summary>
        /// Get a set of files that possibly have a search pattern in the path (such as '*').
        /// </summary>
        /// <param name="searchPath">Search path to find files in.</param>
        /// <param name="fileType">Type of file; typically "Source".</param>
        /// <returns>An array of files matching the search path.</returns>
        /// <remarks>
        /// This method is written in this verbose way because it needs to support ".." in the path.
        /// It needs the directory path isolated from the file name in order to use Directory.GetFiles
        /// or DirectoryInfo.GetFiles.  The only way to get this directory path is manually since
        /// Path.GetDirectoryName does not support ".." in the path.
        /// </remarks>
        /// <exception cref="WixFileNotFoundException">Throws WixFileNotFoundException if no file matching the pattern can be found.</exception>
        public string[] GetFiles(string searchPath, string fileType)
        {
            if (null == searchPath)
            {
                throw new ArgumentNullException(nameof(searchPath));
            }

            // Convert alternate directory separators to the standard one.
            string filePath = searchPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            int lastSeparator = filePath.LastIndexOf(Path.DirectorySeparatorChar);
            string[] files = null;

            try
            {
                if (0 > lastSeparator)
                {
                    files = Directory.GetFiles(".", filePath);
                }
                else // found directory separator
                {
                    files = Directory.GetFiles(filePath.Substring(0, lastSeparator + 1), filePath.Substring(lastSeparator + 1));
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Don't let this function throw the DirectoryNotFoundException. This exception
                // occurs for non-existant directories and invalid characters in the searchPattern.
            }
            catch (ArgumentException)
            {
                // Don't let this function throw the ArgumentException. This exception
                // occurs in certain situations such as when passing a malformed UNC path.
            }
            catch (IOException)
            {
                throw new WixFileNotFoundException(searchPath, fileType);
            }

            if (null == files || 0 == files.Length)
            {
                throw new WixFileNotFoundException(searchPath, fileType);
            }

            return files;
        }

        /// <summary>
        /// Validates that a valid switch (starts with "/" or "-"), and returns a bool indicating its validity
        /// </summary>
        /// <param name="args">The list of strings to check.</param>
        /// <param name="index">The index (in args) of the commandline parameter to be validated.</param>
        /// <returns>True if a valid switch exists there, false if not.</returns>
        public bool IsSwitch(string arg)
        {
            return arg != null && ('/' == arg[0] || '-' == arg[0]);
        }

        /// <summary>
        /// Validates that a valid switch (starts with "/" or "-"), and returns a bool indicating its validity
        /// </summary>
        /// <param name="args">The list of strings to check.</param>
        /// <param name="index">The index (in args) of the commandline parameter to be validated.</param>
        /// <returns>True if a valid switch exists there, false if not.</returns>
        public bool IsSwitchAt(IEnumerable<string> args, int index)
        {
            var arg = args.ElementAtOrDefault(index);
            return IsSwitch(arg);
        }

        public void GetNextArgumentOrError(ref string arg)
        {
            this.TryGetNextArgumentOrError(out arg);
        }

        public void GetNextArgumentOrError(IList<string> args)
        {
            if (this.TryGetNextArgumentOrError(out var arg))
            {
                args.Add(arg);
            }
        }

        public void GetNextArgumentAsFilePathOrError(IList<string> args, string fileType)
        {
            if (this.TryGetNextArgumentOrError(out var arg))
            {
                foreach (var path in this.GetFiles(arg, fileType))
                {
                    args.Add(path);
                }
            }
        }

        public bool TryGetNextArgumentOrError(out string arg)
        {
            //if (this.RemainingArguments.TryDequeue(out arg) && !this.IsSwitch(arg))
            if (TryDequeue(this.RemainingArguments, out arg) && !this.IsSwitch(arg))
            {
                return true;
            }

            this.ErrorArgument = arg ?? CommandLine.ExpectedArgument;

            return false;
        }

        private static bool TryDequeue(Queue<string> q, out string arg)
        {
            if (q.Count > 0)
            {
                arg = q.Dequeue();
                return true;
            }

            arg = null;
            return false;
        }

        private void FlattenArgumentsWithResponseFilesIntoOriginalArguments(string[] commandLineArguments)
        {
            List<string> args = new List<string>();

            foreach (var arg in commandLineArguments)
            {
                if ('@' == arg[0])
                {
                    var responseFileArguments = CommandLine.ParseResponseFile(arg.Substring(1));
                    args.AddRange(responseFileArguments);
                }
                else
                {
                    args.Add(arg);
                }
            }

            this.OriginalArguments = args.ToArray();
        }

        private void QueueArgumentsAndLoadExtensions(string[] args)
        {
            for (var i = 0; i < args.Length; ++i)
            {
                var arg = args[i];

                if ("-ext" == arg || "/ext" == arg)
                {
                    if (!this.IsSwitchAt(args, ++i))
                    {
                        this.ExtensionManager.Load(args[i]);
                    }
                    else
                    {
                        this.ErrorArgument = arg;
                        break;
                    }
                }
                else
                {
                    this.RemainingArguments.Enqueue(arg);
                }
            }
        }

        private void ProcessRemainingArguments(Func<CommandLine, string, bool> parseArgument, Func<CommandLine, string, bool> parseCommand)
        {
            var extensions = this.ExtensionManager.Create<IExtensionCommandLine>();

            while (!this.ShowHelp &&
                   String.IsNullOrEmpty(this.ErrorArgument) &&
                   TryDequeue(this.RemainingArguments, out var arg))
            {
                if (String.IsNullOrWhiteSpace(arg)) // skip blank arguments.
                {
                    continue;
                }

                if ('-' == arg[0] || '/' == arg[0])
                {
                    if (!parseArgument(this, arg) &&
                        !this.TryParseCommandLineArgumentWithExtension(arg, extensions))
                    {
                        this.ErrorArgument = arg;
                    }
                }
                else if (String.IsNullOrEmpty(this.ActiveCommand) && parseCommand != null) // First non-switch must be the command, if commands are supported.
                {
                    if (parseCommand(this, arg))
                    {
                        this.ActiveCommand = arg;
                    }
                    else
                    {
                        this.ErrorArgument = arg;
                    }
                }
                else if (!this.TryParseCommandLineArgumentWithExtension(arg, extensions) &&
                         !parseArgument(this, arg))
                {
                    this.ErrorArgument = arg;
                }
            }
        }

        private bool TryParseCommandLineArgumentWithExtension(string arg, IEnumerable<IExtensionCommandLine> extensions)
        {
            foreach (var extension in extensions)
            {
                //if (extension.ParseArgument(this, arg))
                //{
                //    return true;
                //}
            }

            return false;
        }

        private static List<string> ParseResponseFile(string responseFile)
        {
            string arguments;

            using (StreamReader reader = new StreamReader(responseFile))
            {
                arguments = reader.ReadToEnd();
            }

            return CommandLine.ParseArgumentsToArray(arguments);
        }

        private static List<string> ParseArgumentsToArray(string arguments)
        {
            // Scan and parse the arguments string, dividing up the arguments based on whitespace.
            // Unescaped quotes cause whitespace to be ignored, while the quotes themselves are removed.
            // Quotes may begin and end inside arguments; they don't necessarily just surround whole arguments.
            // Escaped quotes and escaped backslashes also need to be unescaped by this process.

            // Collects the final list of arguments to be returned.
            var argsList = new List<string>();

            // True if we are inside an unescaped quote, meaning whitespace should be ignored.
            var insideQuote = false;

            // Index of the start of the current argument substring; either the start of the argument
            // or the start of a quoted or unquoted sequence within it.
            var partStart = 0;

            // The current argument string being built; when completed it will be added to the list.
            var arg = new StringBuilder();

            for (int i = 0; i <= arguments.Length; i++)
            {
                if (i == arguments.Length || (Char.IsWhiteSpace(arguments[i]) && !insideQuote))
                {
                    // Reached a whitespace separator or the end of the string.

                    // Finish building the current argument.
                    arg.Append(arguments.Substring(partStart, i - partStart));

                    // Skip over the whitespace character.
                    partStart = i + 1;

                    // Add the argument to the list if it's not empty.
                    if (arg.Length > 0)
                    {
                        argsList.Add(CommandLine.ExpandEnvironmentVariables(arg.ToString()));
                        arg.Length = 0;
                    }
                }
                else if (i > partStart && arguments[i - 1] == '\\')
                {
                    // Check the character following an unprocessed backslash.
                    // Unescape quotes, and backslashes followed by a quote.
                    if (arguments[i] == '"' || (arguments[i] == '\\' && arguments.Length > i + 1 && arguments[i + 1] == '"'))
                    {
                        // Unescape the quote or backslash by skipping the preceeding backslash.
                        arg.Append(arguments.Substring(partStart, i - 1 - partStart));
                        arg.Append(arguments[i]);
                        partStart = i + 1;
                    }
                }
                else if (arguments[i] == '"')
                {
                    // Add the quoted or unquoted section to the argument string.
                    arg.Append(arguments.Substring(partStart, i - partStart));

                    // And skip over the quote character.
                    partStart = i + 1;

                    insideQuote = !insideQuote;
                }
            }

            return argsList;
        }

        private static string ExpandEnvironmentVariables(string arguments)
        {
            var id = Environment.GetEnvironmentVariables();

            var regex = new Regex("(?<=\\%)(?:[\\w\\.]+)(?=\\%)");
            MatchCollection matches = regex.Matches(arguments);

            string value = String.Empty;
            for (int i = 0; i <= (matches.Count - 1); i++)
            {
                try
                {
                    var key = matches[i].Value;
                    regex = new Regex(String.Concat("(?i)(?:\\%)(?:", key, ")(?:\\%)"));
                    value = id[key].ToString();
                    arguments = regex.Replace(arguments, value);
                }
                catch (NullReferenceException)
                {
                    // Collapse unresolved environment variables.
                    arguments = regex.Replace(arguments, value);
                }
            }

            return arguments;
        }
    }
}
