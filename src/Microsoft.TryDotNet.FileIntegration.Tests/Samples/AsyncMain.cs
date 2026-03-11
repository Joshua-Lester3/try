using System;
using System.Threading.Tasks;

namespace AsyncMain
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await Task.Delay(1);
            Console.WriteLine("Async main completed");
        }
    }
}
