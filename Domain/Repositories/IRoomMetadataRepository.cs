﻿using System.Collections.Generic;
using RightpointLabs.ConferenceRoom.Domain.Models;
using RightpointLabs.ConferenceRoom.Domain.Models.Entities;

namespace RightpointLabs.ConferenceRoom.Domain.Repositories
{
    public interface IRoomMetadataRepository
    {
        RoomMetadataEntity GetRoomInfo(string roomId);
        RoomMetadataEntity GetRoomInfo(string roomAddress, string organizationId);
        IEnumerable<RoomMetadataEntity> GetRoomInfosForBuilding(string buildingId);
        IEnumerable<RoomMetadataEntity> GetRoomInfosForOrganization(string organizationId);
        void Update(RoomMetadataEntity model);
    }
}