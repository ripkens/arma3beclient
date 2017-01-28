﻿using Arma3BE.Client.Infrastructure.Commands;
using Arma3BE.Client.Infrastructure.Extensions;
using Arma3BE.Client.Infrastructure.Models;
using Arma3BEClient.Libs.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global

namespace Arma3BE.Client.Modules.ChatModule.Models
{
    public class ChatHistoryViewModel : ViewModelBase
    {
        private readonly IServerInfoRepository _repository;

        public ChatHistoryViewModel(Guid serverId, IServerInfoRepository repository)
        {
            _repository = repository;
            FilterCommand = new ActionCommand(async () =>
            {
                try
                {
                    IsBusy = true;
                    await Task.Factory.StartNew(UpdateLog, TaskCreationOptions.LongRunning).ConfigureAwait(true);
                    // ReSharper disable once ExplicitCallerInfoArgument
                    OnPropertyChanged(nameof(Log));
                }
                finally
                {
                    IsBusy = false;
                }
            });

            SelectedServers = serverId.ToString();
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        private void UpdateLog()
        {
            using (var dc = new ChatRepository())
            {
                var log = dc.GetChatLogs(SelectedServers, StartDate.LocalToUtcFromSettings(), EndDate.LocalToUtcFromSettings(), Filter);

                Log = log.OrderBy(x => x.Date).Select(x => new ChatView
                {
                    Id = x.Id,
                    Date = x.Date,
                    ServerName = x.ServerInfo.Name,
                    Text = x.Text
                }).ToList();
            }
        }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string Filter { get; set; }

        public IEnumerable<ChatView> Log { get; private set; }


        public IEnumerable<ServerInfoDto> ServerList => _repository.GetServerInfo().OrderBy(x => x.Name).ToList();

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public string SelectedServers { get; set; }
        public ICommand FilterCommand { get; set; }
    }

    public class ChatView
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public string ServerName { get; set; }
        public DateTime Date { get; set; }
    }
}