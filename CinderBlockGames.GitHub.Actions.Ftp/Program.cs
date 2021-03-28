﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CommandLine;
using FluentFTP;

namespace CinderBlockGames.GitHub.Actions.Ftp
{
    public class Program
    {

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run);
        }

        private static void Run(Options options)
        {
            // Get source files info.
            Console.WriteLine("...Finding source files...");
            var source = Directory.GetFiles(options.SourcePath, "*", SearchOption.AllDirectories)
                                  .Select(src => new Item(src, options.SourcePath));

            using (var client = new FtpClient(options.Server, options.Port, options.Username, options.Password))
            {
                Console.WriteLine("...Connecting to remote server...");
                client.Connect();

                // Get destination files info.
                Console.WriteLine("...Finding destination files...");
                var destination = client.GetListing(options.DestinationPath, FtpListOption.Recursive)
                                        .Where(dest => dest.Type == FtpFileSystemObjectType.File)
                                        .Select(dest => new Item(dest.FullName, options.DestinationPath));

                #region " Delete "

                // Delete any files that don't exist in source.
                var delete = destination.Except(source, ItemComparer.Default);
                Console.WriteLine($"...Deleting {delete.Count()} files...");
                foreach (var file in delete)
                {
                    Console.WriteLine(file.FullPath);
                    client.DeleteFile(file.FullPath);
                }
                Console.WriteLine();

                #endregion

                if (options.SkipUnchanged == true)
                {
                    #region " Update "

                    // Update any files that have changed.
                    Console.WriteLine("...Checking files for updates...");
                    var update = new List<Item>();
                    var existing = (from src in source
                                    from dest in destination
                                    where ItemComparer.Default.Equals(src, dest)
                                    select new { src, dest });
                    using (var algo = MD5.Create())
                    {
                        var filename = Guid.NewGuid().ToString();
                        var encoding = new UnicodeEncoding();
                        foreach (var pair in existing)
                        {
                            if (client.DownloadFile(filename, pair.dest.FullPath) == FtpStatus.Success)
                            {
                                var dest = File.ReadAllText(filename)
                                                .Replace("\r\n", "\n").Replace("\r", "\n"); // Standardize line endings.
                                IEnumerable<byte> hash = algo.ComputeHash(encoding.GetBytes(dest));
                                var src = File.ReadAllText(pair.src.FullPath)
                                                .Replace("\r\n", "\n").Replace("\r", "\n"); // Standardize line endings.
                                if (!hash.SequenceEqual(algo.ComputeHash(encoding.GetBytes(src))))
                                {
                                    update.Add(pair.src);
                                }
                            }
                        }
                    }
                    Console.WriteLine($"...Updating {update.Count()} files...");
                    Upload(client, update);
                    Console.WriteLine();

                    #endregion

                    #region " Insert "

                    // Upload any files that are new.
                    var upload = source.Except(destination, ItemComparer.Default);
                    Console.WriteLine($"...Uploading {upload.Count()} new files...");
                    Upload(client, upload);
                    Console.WriteLine();

                    #endregion
                }
                else
                {
                    #region " Upsert "

                    Console.WriteLine($"...Uploading {source.Count()} files...");
                    Upload(client, source);
                    Console.WriteLine();

                    #endregion
                }

                Console.WriteLine("Complete!");
            }
        }

        private static void Upload(FtpClient client, IEnumerable<Item> files)
        {
            var grouped = files.GroupBy(file => file.Directory, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in grouped)
            {
                Console.WriteLine($"[Directory: {kvp.Key}]");
                Console.WriteLine(string.Join("\r\n", kvp.Select(file => file.FullPath)));
                client.UploadFiles(kvp.Select(file => file.FullPath), kvp.Key);
            }
        }

    }
}
