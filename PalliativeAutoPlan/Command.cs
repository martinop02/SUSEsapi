using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;


    public static class Command
    {
        public static void SendToServer(string cliCommand)
        {
            using (var client = new HttpClient())
            {
                var json = JsonConvert.SerializeObject(new { command = cliCommand });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Use HttpRequestMessage instead of PostAsync with 3 params
                var request = new HttpRequestMessage(HttpMethod.Post, "http://10.92.139.64:2002/run")
                {
                    Content = content
                };

                var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;
                response.EnsureSuccessStatusCode();

                using (var stream = response.Content.ReadAsStreamAsync().Result)
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Console.WriteLine(line);
                    }
                }
            }
        }

    /// <summary>
    /// Checks whether the disk-based server (serverDisk.py) is actually running by
    /// performing a ping/pong handshake through the shared folder.
    /// Writes a "ping.txt" and waits for the server to answer with "pong.txt".
    /// Returns true if the server responded within the timeout, otherwise false.
    /// </summary>
    public static bool IsServerRunning(string sharedFolderPath, int timeoutSeconds = 10)
    {
        string pingFile = Path.Combine(sharedFolderPath, "ping.txt");
        string pongFile = Path.Combine(sharedFolderPath, "pong.txt");

        // Clean any stale pong from a previous check so we only react to a fresh answer
        if (File.Exists(pongFile))
        {
            File.Delete(pongFile);
        }

        // Ask the server to prove it is alive
        File.WriteAllText(pingFile, DateTime.Now.ToString("o"));

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            if (File.Exists(pongFile))
            {
                File.Delete(pongFile);
                return true;
            }

            Thread.Sleep(250); // poll every 250 ms
        }

        // No answer: clean up our unanswered ping so it doesn't linger for next time
        try
        {
            if (File.Exists(pingFile))
            {
                File.Delete(pingFile);
            }
        }
        catch { /* ignore cleanup failures */ }

        return false;
    }

    public static void SaveToDisk(string cliCommand, string sharedFolderPath)
    {
        string commandFile = Path.Combine(sharedFolderPath, "command.txt");
        string doneFile = Path.Combine(sharedFolderPath, "done.txt");

        // Clean any old done file
        if (File.Exists(doneFile))
        {
            File.Delete(doneFile);
        }

        // Write the command to disk
        File.WriteAllText(commandFile, cliCommand);

        // Wait for the server to process and create the done file
        WaitForDoneFile(doneFile, timeout: TimeSpan.FromMinutes(30));

        // Optional: read exit code from done file
        string exitCodeText = File.ReadAllText(doneFile).Trim();
        if (int.TryParse(exitCodeText, out int exitCode))
        {
            Console.WriteLine($"Server reported exit code: {exitCode}");
        }

        // Delete the done file so we are clean for the next command
        File.Delete(doneFile);
    }

    private static void WaitForDoneFile(string doneFilePath, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();

        while (!File.Exists(doneFilePath))
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("Timed out waiting for CLI processing to finish.");
            }

            Thread.Sleep(500); // poll every 500 ms
        }
    }
}
