using LiteNetLib;  
using LiteNetLib.Utils;  
  
class Program  
{  
    static void Main(string[] args)  
    {  
        EventBasedNetListener listener = new EventBasedNetListener();  
        NetManager client = new NetManager(listener);  
        client.Start();  
        client.Connect("localhost", 9050, "SomeConnectionKey");  
  
        listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>  
        {  
            Console.WriteLine("We got: {0}", dataReader.GetString(100));  
            dataReader.Recycle();  
        };  
  
        while (!Console.KeyAvailable)  
        {  
            client.PollEvents();  
            Thread.Sleep(15);  
        }  
  
        client.Stop();  
    }  
}