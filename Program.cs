using Indicoio.SDK;
using Indicoio.SDK.API.Models;
using Indicoio.SDK.Tools;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestInvoicePredict
{
    class Program
    {
        static async Task Main(string[] args)
        {
            const string APIKey = "XXXXXX";
            const string APIHost = @"https://api-demo.indico.io";
            const string FinetuneModel = "1234_5678_9123456789";

            if (args.Length == 0)
            {
                Console.WriteLine("Please supply a path to a pdf file or directory.");
                Environment.Exit(1);
            }

            List<string> validFiles = BuildFileList(args);
            Dictionary<string, string> fileTexts = new Dictionary<string, string>();

            if (validFiles.Count == 0) return;

            Indico sdk = new Indico(APIKey, APIHost);

            // Extract the contents of each file and write to a txt file.

            // Build a list of extraction tasks
            IEnumerable<Task<Tuple<string, string>>> extractTasksQuery = 
                from filePath in validFiles select ExtractDocument(filePath, sdk);

            // Kick off all the tasks in the list
            List<Task<Tuple<string, string>>> extractTasks = extractTasksQuery.ToList();
            while (extractTasks.Count > 0)
            {
                Task<Tuple<string, string>> firstFinishedTask = await Task.WhenAny(extractTasks);
                Tuple<string, string> extractResult = await firstFinishedTask;
                extractTasks.Remove(firstFinishedTask);
                // Skip any failed documents
                if (extractResult.Item2 != null)
                    fileTexts.Add(extractResult.Item1, extractResult.Item2);
            }

            // Load Finetune model
            try
            {
                Console.WriteLine("Loading Model...");
                FinetuneCollection collection = await sdk.finetune.Load(FinetuneModel);
                Console.WriteLine("Model Loaded: {0}", JsonConvert.SerializeObject(collection, Formatting.Indented));
            }
            catch(IndicoAPIException iae)
            {
                Console.WriteLine("Error loading model for predictions. {0}", iae.Message);
                return;
            }
            
            try
            {
                // Get Finetune Extraction Predictions
                List<List<FinetuneExtraction>> allExtractions =
                    await sdk.finetune.PredictAnnotation(FinetuneModel, fileTexts.Values.ToArray());

                int extractionCount = 0;
                string[] extractedFilenames = fileTexts.Keys.ToArray();
                StringBuilder sb = new StringBuilder();
                for (var i = 0; i < allExtractions.Count; i++)
                {
                    string fileName = Path.GetFileName(extractedFilenames[i]);
                    foreach (FinetuneExtraction extraction in allExtractions.ElementAt(i))
                    {
                        string label = extraction.Label;
                        sb.Append(fileName + ",");
                        sb.Append(FormatCSVCell(extraction.Text) + ",");
                        sb.Append(label + ",");
                        sb.Append(extraction.Confidence[label]);
                        sb.AppendLine();
                        extractionCount++;
                    }
                }

                // Write Results to file
                string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "output.csv");
                File.WriteAllText(outputPath, sb.ToString());
                Console.WriteLine("Finetune Extractions complete. {0} extractions found. Report is available here: {1}", extractionCount, outputPath);
            }
            catch(IndicoAPIException iae)
            {
                Console.WriteLine("Error getting predictions for documents. {0}", iae.Message);
                return;
            }
            catch(IOException ioe)
            {
                Console.WriteLine("Error writing predicitons report. {0}", ioe.Message);
            }
        }

        static List<string> BuildFileList(string[] pathArgs)
        {
            List<string> filesFound = new List<string>();

            foreach (string path in pathArgs)
            {
                if (File.Exists(path) && Path.GetExtension(path) == ".pdf")
                {
                    filesFound.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    filesFound = ProcessDirectory(path);
                }
                else
                {
                    Console.WriteLine("{0} is not a valid file or directory.", path);
                }
            }

            return filesFound;
        }

        static List<string> ProcessDirectory(string targetDirectory)
        {
            List<string> filesFound = new List<string>();

            // Process the list of files found in the directory.
            string[] fileEntries = Directory.GetFiles(targetDirectory);
            foreach (string fileName in fileEntries)
            {
                if (Path.GetExtension(fileName) == ".pdf")
                filesFound.Add(fileName);
            }

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
            {
                List<string> subDirFiles = ProcessDirectory(subdirectory);
                filesFound = filesFound.Concat(subDirFiles).ToList();
            }

            return filesFound;
        }

        static async Task<Tuple<string, string>> ExtractDocument(string filePath, Indico sdk)
        {
            string result = null;

            try
            {
                byte[] fileAsBytes = File.ReadAllBytes(filePath);
                string pdfContents = Convert.ToBase64String(fileAsBytes);
                ExtractedPDF contents = await sdk.pdfExtraction.ExtractPDF(pdfContents, true, false, false, false);
                result = contents.RawText;

                string resultsFile = Path.ChangeExtension(filePath, ".txt");
                File.WriteAllText(resultsFile, result);

                Console.WriteLine("Succesfully Extracted {0} and writing to {1}.", filePath, resultsFile); 
            }
            catch (IndicoAPIException ie)
            {
                Console.WriteLine("Error Extracting {0}. {2}", filePath, ie.Message);
            }

            return Tuple.Create(filePath, result);
        }

        static string FormatCSVCell(string input)
        {
            return String.Format(@"""{0}""", input.Replace("\"", "\"\""));
        }

    }
}
