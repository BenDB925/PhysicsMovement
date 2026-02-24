using NUnit.Framework;
using PhysicsDrivenMovement.AI;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="MuseumNavigator"/> BFS pathfinding
    /// and room detection. No physics required — pure logic tests.
    /// </summary>
    public class MuseumNavigatorTests
    {
        // ─── Room Detection ───────────────────────────────────────────────────

        [Test]
        public void GetRoomIndex_PointInLobby_ReturnsLobby()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(0f, 0f, 0f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.Lobby));
        }

        [Test]
        public void GetRoomIndex_PointInSculptureHall_ReturnsSculptureHall()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(0f, 0f, 12f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.SculptureHall));
        }

        [Test]
        public void GetRoomIndex_PointInWestGallery_ReturnsWestGallery()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(-13f, 0f, 12f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.WestGallery));
        }

        [Test]
        public void GetRoomIndex_PointInEastGallery_ReturnsEastGallery()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(13f, 0f, 12f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.EastGallery));
        }

        [Test]
        public void GetRoomIndex_PointInStorageRoom_ReturnsStorageRoom()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(-13f, 0f, 0f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.StorageRoom));
        }

        [Test]
        public void GetRoomIndex_PointInSecurityOffice_ReturnsSecurityOffice()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(13f, 0f, 0f));
            Assert.That(room, Is.EqualTo(MuseumNavigator.SecurityOffice));
        }

        [Test]
        public void GetRoomIndex_PointOutsideAllRooms_ReturnsNegativeOne()
        {
            int room = MuseumNavigator.GetRoomIndex(new Vector3(100f, 0f, 100f));
            Assert.That(room, Is.EqualTo(-1));
        }

        // ─── Pathfinding ──────────────────────────────────────────────────────

        [Test]
        public void FindPath_SameRoom_ReturnsEmptyList()
        {
            var path = MuseumNavigator.FindPath(
                new Vector3(0f, 0f, 0f),   // Lobby
                new Vector3(2f, 0f, 2f));  // Also Lobby

            Assert.That(path, Is.Not.Null);
            Assert.That(path.Count, Is.EqualTo(0));
        }

        [Test]
        public void FindPath_LobbyToSculptureHall_ReturnsSingleDoor()
        {
            var path = MuseumNavigator.FindPath(
                new Vector3(0f, 0f, 0f),    // Lobby
                new Vector3(0f, 0f, 12f));  // Sculpture Hall

            Assert.That(path, Is.Not.Null);
            Assert.That(path.Count, Is.EqualTo(1));
            // Door between Lobby and Sculpture Hall is at (0, 0, 6).
            Assert.That(path[0].x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(path[0].z, Is.EqualTo(6f).Within(0.01f));
        }

        [Test]
        public void FindPath_LobbyToWestGallery_ReturnsTwoDoors()
        {
            var path = MuseumNavigator.FindPath(
                new Vector3(0f, 0f, 0f),     // Lobby
                new Vector3(-13f, 0f, 12f)); // West Gallery

            Assert.That(path, Is.Not.Null);
            // Should go through Lobby->SculptureHall or Lobby->Storage->WestGallery (2 doors either way).
            Assert.That(path.Count, Is.EqualTo(2));
        }

        [Test]
        public void FindPath_StorageToEastGallery_ReturnsBFSShortestPath()
        {
            var path = MuseumNavigator.FindPath(
                new Vector3(-13f, 0f, 0f),  // Storage Room
                new Vector3(13f, 0f, 12f)); // East Gallery

            Assert.That(path, Is.Not.Null);
            // Storage -> Lobby -> SculptureHall -> EastGallery = 3 doors
            // OR Storage -> WestGallery -> SculptureHall -> EastGallery = 3 doors
            Assert.That(path.Count, Is.EqualTo(3));
        }

        [Test]
        public void FindPath_PointOutsideRooms_ReturnsNull()
        {
            var path = MuseumNavigator.FindPath(
                new Vector3(100f, 0f, 100f),
                new Vector3(0f, 0f, 0f));

            Assert.That(path, Is.Null);
        }

        // ─── Room Center ──────────────────────────────────────────────────────

        [Test]
        public void GetRoomCenter_Lobby_ReturnsCorrectCenter()
        {
            Vector3 center = MuseumNavigator.GetRoomCenter(MuseumNavigator.Lobby);
            Assert.That(center.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(center.z, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void GetRoomCenter_SculptureHall_ReturnsCorrectCenter()
        {
            Vector3 center = MuseumNavigator.GetRoomCenter(MuseumNavigator.SculptureHall);
            Assert.That(center.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(center.z, Is.EqualTo(12f).Within(0.01f));
        }

        [Test]
        public void GetRoomCenter_InvalidIndex_ReturnsZero()
        {
            Vector3 center = MuseumNavigator.GetRoomCenter(-1);
            Assert.That(center, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GetRoomName_ValidIndex_ReturnsName()
        {
            Assert.That(MuseumNavigator.GetRoomName(MuseumNavigator.Lobby), Is.EqualTo("Main Lobby"));
            Assert.That(MuseumNavigator.GetRoomName(MuseumNavigator.SculptureHall), Is.EqualTo("Sculpture Hall"));
        }
    }
}
