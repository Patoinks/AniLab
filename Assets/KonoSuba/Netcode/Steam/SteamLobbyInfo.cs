using System;
using AnimLab.Netcode.Sim;
using AnimLab.Netcode.Rollback;

namespace AnimLab.Netcode.Steam
{
    public sealed class SteamLobbyInfo
    {
        public const string GameKey="animlab_game",ProtocolKey="animlab_protocol",BuildKey="animlab_build";
        public const string Game="AnimLab.7c92810e6a1b4b949532e6e01e597dac";
        // Incrementar quando mudar a simulação ou o formato da rede.
        public const string Build="rollback-steam-6.1";
        // Inclui a impressão digital das regras: só aparecem lobbies de quem tem exatamente o mesmo código.
        public static string Protocol => "wire1.rules"+MatchHandshake.RulesVersion+"."+RulesFingerprint.Value.ToString("X16")+".state"+StateCodec.Version;
        public ulong Id,Owner;
        public string Host,Status;
        public Style Style;
        public int Members,Limit;
        public bool CanJoin => Status=="Waiting" && Members==1 && Limit==2;
        public static bool TryRead(ulong id,ulong owner,int members,int limit,Func<string,string> data,out SteamLobbyInfo lobby)
        {
            lobby=null;
            if(id==0 || owner==0 || limit!=2 || members<1 || members>2 || data(GameKey)!=Game || data(ProtocolKey)!=Protocol || data(BuildKey)!=Build) return false;
            if(data("host_id")!=owner.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
            string style=data("style"),status=data("state"),host=data("host");
            if(string.IsNullOrWhiteSpace(host) || host.Length>128 || (status!="Waiting" && status!="Connecting" && status!="Playing")) return false;
            if(!Enum.TryParse(style,out Style parsed) || !Enum.IsDefined(typeof(Style),parsed) || parsed==Style.Iaido || parsed.ToString()!=style) return false;
            lobby=new SteamLobbyInfo { Id=id,Owner=owner,Host=host,Style=parsed,Status=status,Members=members,Limit=limit }; return true;
        }
    }
}
