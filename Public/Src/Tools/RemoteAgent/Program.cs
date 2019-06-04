// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;

namespace RemoteAgent
{
    public class Program
    {

        public static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("DebugRemoteAgentOnStart") == "1" ||
                args?.Any(arg => string.Equals(arg, "/debug", StringComparison.OrdinalIgnoreCase)) == true)
            {
                Console.WriteLine("You can now attach a debugger. Press enter to continue.");
                Console.ReadLine();
            }

            var success = RunServiceAsync("v:", 2233).GetAwaiter().GetResult();

            return success ? 0 : 1;
        }

        public static async Task<bool> RunServiceAsync(string root, int port)
        {
            using (var agent = new RemoteAgent())
            {
                Console.WriteLine("Initializing....");
                await agent.StartAsync(root, port);
                
                Console.WriteLine("Running.... Press <enter> to exit.");
                Console.ReadLine();

                await agent.ShutDownAsync();
            }

            return true;
        }
    }
}