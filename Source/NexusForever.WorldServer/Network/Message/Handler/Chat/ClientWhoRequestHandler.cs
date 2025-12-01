using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Who;
using NexusForever.Network.Message;
using NexusForever.Network.Session;
using NexusForever.Network.World.Message.Model.Who;
using NexusForever.Network.World.Message.Model.Who.Parameter;
using NexusForever.Shared;
using System.Collections.Generic;

namespace NexusForever.WorldServer.Network.Message.Handler.Chat
{
    public class ClientWhoRequestHandler : IMessageHandler<IWorldSession, ClientWhoRequest>
    {
        private enum FilterStrategy
        {
            Name,
            Race,
            Class,
            Path,
            Zone,
            Level
        }

        private readonly bool shouldSearchesIncludeThePlayerInitiatingSearch = true;
        private readonly bool shouldSearchesIncludeOppositeFaction = true;
        public void HandleMessage(IWorldSession requestingSession, ClientWhoRequest request)
        {
            INetworkManager<IWorldSession> worldSessions = LegacyServiceProvider.Provider.GetService<INetworkManager<IWorldSession>>();

            List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList = new List<ServerWhoResponse.WhoPlayer>();
            WhoParameter? currentRequestParameter = request.Parameters.Count > 0 ? request.Parameters[0] : null;

            // Iterate over sessions (connected clients) for filtering.
            foreach (IWorldSession sessionCandidate in worldSessions)
            {
                if (currentRequestParameter == null)
                {
                    AddPlayerToList(requestingSession, sessionCandidate, whoResponsePlayerList);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Combo)
                {
                    WhoParameterCombo comboData = currentRequestParameter.Data as WhoParameterCombo;
                    FilterByCombo(requestingSession, sessionCandidate, whoResponsePlayerList, comboData);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Level)
                {
                    WhoParameterLevel levelData = currentRequestParameter.Data as WhoParameterLevel;
                    FilterByLevel(requestingSession, sessionCandidate, whoResponsePlayerList, levelData);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Race)
                {
                    WhoParameterRace raceData = currentRequestParameter.Data as WhoParameterRace;
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, raceData.RaceId, sessionCandidate.Player.Race);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Path)
                {
                    WhoParameterPath pathData = currentRequestParameter.Data as WhoParameterPath;
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, pathData.PathId, sessionCandidate.Player.Path);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Class)
                {
                    WhoParameterClass classData = currentRequestParameter.Data as WhoParameterClass;
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, classData.ClassId, sessionCandidate.Player.Class);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Zone)
                {
                    WhoParameterZone zoneData = currentRequestParameter.Data as WhoParameterZone;
                    FilterByZone(requestingSession, sessionCandidate, whoResponsePlayerList, zoneData.WorldZoneId, sessionCandidate.Player);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Player)
                {
                    WhoParameterPlayer playerData = currentRequestParameter.Data as WhoParameterPlayer;
                    FilterByName(requestingSession, sessionCandidate, whoResponsePlayerList, playerData.PlayerName, sessionCandidate.Player.Name);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Guild)
                {
                    WhoParameterGuild guildData = currentRequestParameter.Data as WhoParameterGuild;
                    FilterByName(requestingSession, sessionCandidate, whoResponsePlayerList, guildData.GuildName, sessionCandidate.Player.GuildManager.Guild.Name);
                }
                else if (currentRequestParameter.Type == WhoParameterType.Faction)
                {
                    WhoParameterFaction factionData = currentRequestParameter.Data as WhoParameterFaction;
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, factionData.Faction2Id, sessionCandidate.Player.Faction2);
                }
            }

            requestingSession.EnqueueMessageEncrypted(new ServerWhoResponse
            {
                Players = whoResponsePlayerList
            });
        }

        private void FilterByLevel(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList, WhoParameterLevel levelData)
        {
            IPlayer candidatePlayer = sessionCandidate.Player;
            uint candidateLevel = candidatePlayer.Level;
            if (candidateLevel >= levelData.BottomLevel && candidateLevel < levelData.TopLevel)
            {
                AddPlayerToList(requestingSession, sessionCandidate, whoResponsePlayerList);
            }
        }

        private void FilterByCombo(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList, WhoParameterCombo comboData)
        {
            IPlayer candidatePlayer = sessionCandidate.Player;
            FilterStrategy inferredFilterStrategy = FilterStrategy.Name;

            if (comboData.RaceId != Race.None)
            {
                inferredFilterStrategy = FilterStrategy.Race;
            }
            else if (comboData.ClassId != Class.None)
            {
                inferredFilterStrategy = FilterStrategy.Class;
            }
            else if (comboData.PathId != Game.Static.Entity.Path.None)
            {
                inferredFilterStrategy = FilterStrategy.Path;
            }
            else if (comboData.WorldZoneId != 0)
            {
                inferredFilterStrategy = FilterStrategy.Zone;
            }

            switch (inferredFilterStrategy)
            {
                case FilterStrategy.Name:
                    FilterByName(requestingSession, sessionCandidate, whoResponsePlayerList, comboData.SearchString, candidatePlayer.Name);
                    break;
                case FilterStrategy.Race:
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, comboData.RaceId, candidatePlayer.Race);
                    break;
                case FilterStrategy.Class:
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, comboData.ClassId, candidatePlayer.Class);
                    break;
                case FilterStrategy.Path:
                    FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, comboData.PathId, candidatePlayer.Path);
                    break;
                case FilterStrategy.Zone:
                    FilterByZone(requestingSession, sessionCandidate, whoResponsePlayerList, comboData.WorldZoneId, candidatePlayer);
                    break;
            }
        }

        private void FilterByZone(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList, uint worldZoneId, IPlayer candidatePlayer)
        {
            // WIP: It's not clear how ComboWhoRequests interact with the subzones. I have not been able to get a combo sub-zone request through via the client.
            // For now, if you're in a subzone, we will compare your parent zone id with the search query, since combo search querys seem to only support these.
            uint subZoneId = candidatePlayer.Zone.Id;
            uint parentZoneId = candidatePlayer.Zone.ParentZoneId;

            if (parentZoneId == 0)
            {
                FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, worldZoneId, candidatePlayer.Zone.Id);
            }
            else
            {
                FilterByArgs(requestingSession, sessionCandidate, whoResponsePlayerList, worldZoneId, candidatePlayer.Zone.ParentZoneId);
            }
        }

        private bool FilterByArgs<T>(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList, T filterValue, T candidateValue)
        {
            if (EqualityComparer<T>.Default.Equals(filterValue, candidateValue))
            {
                AddPlayerToList(requestingSession, sessionCandidate, whoResponsePlayerList);
                return true;
            }
            return false;
        }

        private bool FilterByName(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList, string filterString, string candidateName)
        {
            // We want to filter in a case insensitive way
            string lowerFilterString = filterString.ToLower();
            string lowerCandidateName = candidateName.ToLower();

            if (lowerCandidateName.IndexOf(lowerFilterString) != -1)
            {
                AddPlayerToList(requestingSession, sessionCandidate, whoResponsePlayerList);
                return true;
            }
            return false;
        }

        private void AddPlayerToList(IWorldSession requestingSession, IWorldSession sessionCandidate, List<ServerWhoResponse.WhoPlayer> whoResponsePlayerList)
        {
            if (requestingSession.Id == sessionCandidate.Id && (shouldSearchesIncludeThePlayerInitiatingSearch == false))
            {
                // This code exits early if searching party's session ID is matched with one of the filtered client session's IDs.
                return;
            }

            if (requestingSession.Player.Faction2 != sessionCandidate.Player.Faction2 && (shouldSearchesIncludeOppositeFaction == false))
            {
                // Faction2 is the immutable faction. Faction1 can change based on the instance to support cross faction play.
                // This code exits early if searching player's faction does not match the candidate player faction.
                return;
            }

            IPlayer sessionPlayer = sessionCandidate.Player;
            whoResponsePlayerList.Add(new()
            {
                Name = sessionPlayer.Name,
                Level = sessionPlayer.Level,
                Race = sessionPlayer.Race,
                Class = sessionPlayer.Class,
                Path = sessionPlayer.Path,
                Faction = sessionPlayer.Faction1,
                Sex = sessionPlayer.Sex,
                Zone = sessionPlayer.Zone.Id
            });
        }
    }
}
