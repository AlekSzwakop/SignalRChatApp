using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using API.Data;
using API.DTOs;
using API.Models;

namespace API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly UserManager<AppUsers> _userManager;
        private readonly AppDbContext _context;
        public static readonly ConcurrentDictionary<string, OnlineUserDto> onlineUsers = new();

        public ChatHub(UserManager<AppUsers> userManager, AppDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task SendMessage(MessageRequestDto message)
        {
            var senderName = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(senderName)) return;
            if (string.IsNullOrEmpty(message.ReceiverId)) return;
            var senderUser = await _userManager.FindByNameAsync(senderName);
            if (senderUser == null) return;
            var receiverUser = await _userManager.FindByIdAsync(message.ReceiverId);
            if (receiverUser == null) return;

            var newMsg = new Message
            {
                Sender = senderUser,
                Receiver = receiverUser,
                IsRead = false,
                CreatedDate = DateTime.UtcNow,
                Content = message.Content
            };
            _context.Messages.Add(newMsg);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(receiverUser.UserName))
            {
                await Clients.User(receiverUser.UserName).SendAsync("ReceiveNewMessage", newMsg);
            }
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var querySenderId = httpContext?.Request.Query["senderId"].ToString();
            var userName = Context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userName))
            {
                var currentUser = await _userManager.FindByNameAsync(userName);
                var connectionId = Context.ConnectionId;
                if (onlineUsers.ContainsKey(userName))
                {
                    onlineUsers[userName].ConnectionId = connectionId;
                }
                else
                {
                    var userDto = new OnlineUserDto
                    {
                        ConnectionId = connectionId,
                        UserName = userName,
                        ProfilePicture = currentUser?.ProfileImage,
                        FullName = currentUser?.FullName
                    };
                    onlineUsers.TryAdd(userName, userDto);
                    await Clients.AllExcept(connectionId).SendAsync("UserConnected", currentUser);
                }
                if (!string.IsNullOrEmpty(querySenderId))
                {
                    await LoadMessages(querySenderId);
                }
                await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
            }
            await base.OnConnectedAsync();
        }

        public async Task NotifyTyping(string recipientUserName)
        {
            var senderName = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(senderName)) return;
            var connectionId = onlineUsers.Values.FirstOrDefault(x => x.UserName == recipientUserName)?.ConnectionId;
            if (!string.IsNullOrEmpty(connectionId))
            {
                await Clients.Client(connectionId).SendAsync("NotifyTypingToUser", senderName);
            }
        }

        public async Task LoadMessages(string recipientId, int pageNumber = 1)
        {
            var senderName = Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(senderName)) return;
            if (string.IsNullOrEmpty(recipientId)) return;

            var currentUser = await _userManager.FindByNameAsync(senderName);
            if (currentUser == null) return;
            int pageSize = 10;

            var messages = await _context.Messages
                .Where(x =>
                    x.Sender != null &&
                    x.Receiver != null &&
                    ((x.Sender.Id == currentUser.Id && x.Receiver.Id == recipientId) ||
                     (x.Receiver.Id == currentUser.Id && x.Sender.Id == recipientId))
                )
                .OrderByDescending(x => x.CreatedDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .OrderBy(x => x.CreatedDate)
                .Select(x => new MessageResponseDto
                {
                    Id = x.Id,
                    Content = x.Content,
                    CreatedDate = x.CreatedDate,
                    ReceiverId = x.Receiver!.Id,
                    SenderId = x.Sender!.Id
                })
                .ToListAsync();

            foreach (var message in messages)
            {
                var msg = await _context.Messages.FirstOrDefaultAsync(x => x.Id == message.Id);
                if (msg != null && msg.Receiver != null && msg.Receiver.Id == currentUser.Id && !msg.IsRead)
                {
                    msg.IsRead = true;
                    await _context.SaveChangesAsync();
                }
            }

            if (!string.IsNullOrEmpty(currentUser.UserName))
            {
                await Clients.User(currentUser.UserName).SendAsync("ReceiveMessageList", messages);
            }
        }

        public override async Task OnDisconnectedAsync(System.Exception? exception)
        {
            var userName = Context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userName))
            {
                onlineUsers.TryRemove(userName, out _);
                await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
            }
            await base.OnDisconnectedAsync(exception);
        }

        private async Task<IEnumerable<OnlineUserDto>> GetAllUsers()
        {
            var onlineKeys = new HashSet<string>(onlineUsers.Keys);
            var currentUserName = Context.User?.Identity?.Name;
            var users = await _userManager.Users
                .Select(u => new OnlineUserDto
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    FullName = u.FullName,
                    ProfilePicture = u.ProfileImage,
                    IsOnline = !string.IsNullOrEmpty(u.UserName) && onlineKeys.Contains(u.UserName),
                    UnreadCount = _context.Messages.Count(
                        x =>
                            x.Sender != null &&
                            x.Sender.UserName == u.UserName &&
                            x.Receiver != null &&
                            x.Receiver.UserName == currentUserName &&
                            !x.IsRead
                    )
                })
                .OrderByDescending(u => u.IsOnline)
                .ToListAsync();
            return users;
        }
    }
}
