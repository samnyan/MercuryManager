using System.Security.Cryptography;
namespace MercuryManager.Server;
public static class FileTransaction
{
    public static void Copy(IReadOnlyList<(string Source,string Destination)> files,bool backup)
    {
        string token=".mercury-"+Guid.NewGuid().ToString("N");var moved=new List<string>();var old=new List<string>();var backups=new List<string>();
        foreach(var f in files){MusicWorkspaceStore.RejectLinks(f.Source);MusicWorkspaceStore.RejectLinks(f.Destination);if(backup){MusicWorkspaceStore.RejectLinks(f.Destination+"_bak");if(File.Exists(f.Destination+"_bak"))throw new IOException("Backup already exists.");}}
        try
        {
            foreach(var f in files){Directory.CreateDirectory(Path.GetDirectoryName(f.Destination)!);File.Copy(f.Source,f.Destination+token+".new",false);using var a=File.OpenRead(f.Source);using var b=File.OpenRead(f.Destination+token+".new");if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))throw new IOException("Copy verification failed.");}
            foreach(var f in files){if(File.Exists(f.Destination)){File.Move(f.Destination,f.Destination+token+".old");old.Add(f.Destination);}File.Move(f.Destination+token+".new",f.Destination);moved.Add(f.Destination);}
            if(backup)foreach(var dest in old){File.Copy(dest+token+".old",dest+"_bak",false);backups.Add(dest+"_bak");}
        }
        catch
        {
            foreach(var dest in moved)File.Delete(dest);foreach(var dest in old)File.Move(dest+token+".old",dest);foreach(var dest in backups)File.Delete(dest);throw;
        }
        finally{foreach(var f in files)File.Delete(f.Destination+token+".new");}
        foreach(var dest in old)File.Delete(dest+token+".old");
    }
}
