using System;

namespace Fibonacci
{
    class Program
    {
        static void Main(string[] args)
        {
            int n = 10;
            int a = 0, b = 1;
            for (int i = 0; i < n; i++)
            {
                Console.WriteLine(a);
                int temp = a;
                a = b;
                b = temp + b;
            }
        }
    }
}
