using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;

using AppointmentContext = System.Collections.Generic.Dictionary<Discord.IMessageChannel, System.Collections.Generic.List<SeerRaidBot.RaidAppointment>>;

namespace SeerRaidBot {
  class Program {
    static readonly TimeSpan REGISTER_DELTA = TimeSpan.FromHours(2);
    static readonly TimeSpan    ALERT_DELTA = TimeSpan.FromMinutes(15);
    static readonly string SAVE_FILE = "data.save";
    static readonly int    SAVE_VERSION = 1;

    static DiscordSocketClient client;
    
    static Dictionary<IGuild, AppointmentContext> context_dict;

    //https://discord.com/api/oauth2/authorize?client_id=866727722835116033&permissions=2048&scope=bot%20applications.commands
    static void Main(string[] args) {
      context_dict = new Dictionary<IGuild, AppointmentContext>();
      //todo: load next id
      
      string token = File.ReadAllText("token.txt");

      client = new DiscordSocketClient(new DiscordSocketConfig() {
        AlwaysAcknowledgeInteractions = false,
      });
      client.Log                += log;
      client.InteractionCreated += interaction_created;
      client.Ready              += client_ready;
      client.JoinedGuild        += joined_guild;
      client.LeftGuild          += left_guild;

      client.LoginAsync(TokenType.Bot, token).Wait();
      client.StartAsync().Wait();

      if (!Console.IsInputRedirected)
      {
        while (Console.ReadLine() != "exit")
          Console.WriteLine("write 'exit' to exit.");

        client.LogoutAsync().Wait();
      }
      else
      {
        Thread.Sleep(Timeout.Infinite);
      }
    }
    private static async Task joined_guild(SocketGuild guild)
    {
      context_dict.Add(guild, new AppointmentContext());
    }
    private static async Task left_guild(SocketGuild guild)
    {
      context_dict.Remove(guild);
    }

    public static async Task client_ready()
    {
      if(load())
      {
        Console.WriteLine($"loaded {context_dict.Count} guilds data");
      }
      else
      {
        Console.WriteLine("load failed");
      }
      
      foreach (var guild in client.Guilds)
      {
        context_dict.TryAdd(guild, new AppointmentContext());
      }

      var test_id = 314748402682429442uL; //todo @nockeckin

      var builders = new [] {
        new SlashCommandBuilder()
          .WithName("raid")
          .WithDescription("manage raid alerts")
          .AddOption(new SlashCommandOptionBuilder()
            .WithName("add")
            .WithType(ApplicationCommandOptionType.SubCommand)
            .WithDescription("add a raid event")
            .AddOption("initial-occurence", ApplicationCommandOptionType.String, "the first occurance of the raid event (format: ddTT:TT-TT:TT)")
            .AddOption("interval", ApplicationCommandOptionType.Integer, "amount of days after which the event repeats")
            .AddOption("text", ApplicationCommandOptionType.String, "custom text to add the the alert", required: false)
          )
          .AddOption(new SlashCommandOptionBuilder()
            .WithName("trigger")
            .WithType(ApplicationCommandOptionType.SubCommand)
            .WithDescription("trigger a message")
            .AddOption(new SlashCommandOptionBuilder()
              .WithName("what")
              .WithType(ApplicationCommandOptionType.Integer)
              .AddChoice("register", 1)
              .AddChoice("alert", 2)
              .WithDescription("what to trigger")
              .WithRequired(true)
            )
            .AddOption("aid", ApplicationCommandOptionType.Integer, "appointment id")
          )
          .AddOption(new SlashCommandOptionBuilder()
            .WithName("trigger-reset")
            .WithType(ApplicationCommandOptionType.SubCommand)
            .WithDescription("reset a message trigger")
            .AddOption(new SlashCommandOptionBuilder()
              .WithName("what")
              .WithType(ApplicationCommandOptionType.Integer)
              .AddChoice("register", 1)
              .AddChoice("alert", 2)
              .WithDescription("what to trigger")
              .WithRequired(true)
            )
            .AddOption("aid", ApplicationCommandOptionType.Integer, "appointment id")
          ),
      };
      foreach (var builder in builders)
      {
        try
        {
          //client.Rest.CreateGlobalCommand(builder.Build()).Wait();
          var result = client.Rest.CreateGuildCommand(builder.Build(), test_id).Result;
        }
        catch(ApplicationCommandException ex)
        {
          var json = JsonConvert.SerializeObject(ex.Error, Formatting.Indented);
          Console.WriteLine(json);
        }
      }
    }

    private static async Task interaction_created(SocketInteraction arg)
    {
      var command = arg as SocketSlashCommand;
      if(command == null) return;

      if(command.Data.Options.Count != 1)
      {
        command.RespondAsync($"unknown command '{command.Data.Name}'", ephemeral: true).Wait();
        return;
      }

      var sub_command = command.Data.Options.First();
      switch (sub_command.Name)
      {
        case "add": {
          if(sub_command.Options.Count < 2)
          {
            command.RespondAsync("add requires at least two options", ephemeral: true).Wait();
            return;
          }
          var options = sub_command.Options.ToArray();
          add(command, context_dict[((SocketGuildChannel)command.Channel).Guild],
            (string)options[0].Value, (int)options[1].Value, options.Length > 2 ? (string)options[2].Value : null);
        } return;
        case "trigger": {
          var options = sub_command.Options.ToArray();
          if(options.Length < 2)
          {
            command.RespondAsync("trigger needs two arguments", ephemeral: true).Wait();
            return;
          }
          switch ((int)options[0].Value)
          {
            case 1: {
              var id = (int)options[1].Value;
              var appointment = context_dict[((SocketGuildChannel)command.Channel).Guild][command.Channel].First(a => a.ID == id);
              register(command.Channel, appointment);
              command.RespondAsync("success", ephemeral: true).Wait();
            } break;
            case 2: {
              var id = (int)options[1].Value;
              var appointment = context_dict[((SocketGuildChannel)command.Channel).Guild][command.Channel].First(a => a.ID == id);
              if(appointment.last_message_register == null)
              {
                command.RespondAsync("this event had no register message yet.", ephemeral: true).Wait();
              }
              else
              {
                alert(command.Channel, appointment);
                command.RespondAsync("success", ephemeral: true).Wait();
              }
            } break;
            default: {
              command.RespondAsync($"unknown what 'trigger what: {options[0].Value}'", ephemeral: true).Wait();
            } break;
          }
        } return;
        case "trigger-reset": {
          var options = sub_command.Options.ToArray();
          if(options.Length < 2)
          {
            command.RespondAsync("trigger needs two arguments", ephemeral: true).Wait();
            return;
          }
          switch ((int)options[0].Value)
          {
            case 1: {
              var id = (int)options[1].Value;
              var appointment = context_dict[((SocketGuildChannel)command.Channel).Guild][command.Channel].First(a => a.ID == id);
              appointment.last_message_register = null;
              command.RespondAsync("success", ephemeral: true).Wait();
            } break;
            case 2: {
              var id = (int)options[1].Value;
              var appointment = context_dict[((SocketGuildChannel)command.Channel).Guild][command.Channel].First(a => a.ID == id);
              appointment.last_message_alert = null;
              command.RespondAsync("success", ephemeral: true).Wait();
            } break;
            default: {
              command.RespondAsync($"unknown what 'trigger what: {options[0].Value}'", ephemeral: true).Wait();
            } return;
          }
          save();
        } return;
        default: {
          command.RespondAsync($"unknown sub-command '{sub_command.Name}'", ephemeral: true).Wait();
        } return;
      }
    }

    private static async Task handle_message(SocketMessage message)
    {
      var user_message = message as SocketUserMessage;
      if(message == null || message.Author.IsBot) return;

      var user = message.Author as SocketGuildUser;
      //foreach (var role in user.Roles)
      //{
      //  if(role.Name == "KULTIST" || role.Name == "Seeker")
      //  {
      //    goto good;
      //  }
      //}
      //return;
    }

    static void tick()
    {
      foreach (var (guild, context) in context_dict)
      {
        foreach (var (channel, appointments) in context)
        {
          foreach (var appointment in appointments)
          {
            if(appointment.last_message_alert == null &&
               appointment.next_occurence - DateTime.Now < ALERT_DELTA)
            {
              alert(channel, appointment);
              appointment.last_message_register = null;
            }
            if(appointment.last_message_register == null &&
               DateTime.Now - appointment.next_occurence < REGISTER_DELTA)
            {
              appointment.next_occurence += appointment.interval;
              register(channel, appointment);
              appointment.last_message_alert = null;
            }
          }
        }
      }
    }

    public static void add(SocketSlashCommand src_command, AppointmentContext context, string first_occurence, int interval, string custom_text)
    {
      var day_slice = first_occurence[..2].ToLower();
      DayOfWeek day;
      switch (day_slice)
      {
        case "mo": day = DayOfWeek.Monday;    break;
        case "tu": day = DayOfWeek.Tuesday;   break;
        case "we": day = DayOfWeek.Wednesday; break;
        case "th": day = DayOfWeek.Thursday;  break;
        case "fr": day = DayOfWeek.Friday;    break;
        case "sa": day = DayOfWeek.Saturday;  break;
        case "su": day = DayOfWeek.Sunday;    break;
        default: src_command.RespondAsync($"invalid day of the week '{day_slice}'", ephemeral: true); return;
      }
      var start_time = first_occurence.Substring(2, 5);
      var   end_time = first_occurence.Substring(8, 5);
      var now = DateTime.Now;
      var days = (day - now.DayOfWeek + 7) % 7;

      var start_day = now.AddDays(days);
      var start_timestamp = new DateTime(
        start_day.Year, start_day.Month, start_day.Day, 
        int.Parse(start_time[..2]), int.Parse(start_time[3..]), 0
      );
      var end_timestamp = new DateTime(
        start_day.Year, start_day.Month, start_day.Day, 
        int.Parse(end_time[..2]), int.Parse(end_time[3..]), 0
      );

      if(!context.TryGetValue(src_command.Channel, out var appointments))
      {
        appointments = new List<RaidAppointment>();
        context.Add(src_command.Channel, appointments);
      }
      var appointment = new RaidAppointment() {
        ID                = RaidAppointment.next_ID++,
        appointment_start = start_timestamp,
        appointment_end   = end_timestamp,
        interval          = TimeSpan.FromDays(interval),
        custom_message    = custom_text,
      };
      appointments.Add(appointment);

      src_command.RespondAsync($"Successfully added a new raid event with ID {appointment.ID}.\n" +
                               $"The first register message will be sent shortly, and from then on {REGISTER_DELTA} after the end of the last occurence.\n" +
                               $"An alert will be sent {ALERT_DELTA} before the event.").Wait();

      register(src_command.Channel, appointment);

      //save(); //NOTE(Rennorb): already done in register
    }

    static ulong[] allowed_emotes = {
      757142853049909339, //:Necromancer:
      757142853682987068, //:Reaper:
      757142853502763130, //:Scourge:
      757142853490180166, //:Thief:
      757142853032869900, //:Daredevil:
      757142853322276965, //:Deadeye:
      757142853767135292, //:Mesmer:
      757142852596924479, //:Chronomancer:
      757142853431459910, //:Mirage:
      757142853531992094, //:Engineer:
      757142853607489546, //:Scrapper:
      757142853515477112, //:Holosmith:
      757142853393711184, //:Elementalist:
      757142853431328839, //:Weaver:
      757142853628723251, //:Tempest:
      757142853213487247, //:Ranger:
      757142853439979560, //:Druid:
      757142853557420092, //:Soulbeast:
      757142853431590982, //:Revenant:
      757142853456494642, //:Renegade:
      757142853309825105, //:Herald:
      757142853175476265, //:Guardian:
      757142852986732576, //:Dragonhunter:
      757142853435523122, //:Firebrand:
      757142853531992114, //:Warrior:
      757142853012029490, //:Berserker:
      757142853477728308, //:Spellbreaker:
    }; 

    public static void register(IMessageChannel channel, RaidAppointment appointment)
    {
      var builder = new EmbedBuilder() {
        Title        = "Raid register",
        ThumbnailUrl = "https://wiki.guildwars2.com/images/5/5e/Commander_tag_%28green%29.png",
      };
      if(appointment.custom_message != null)
      {
        builder.WithDescription(appointment.custom_message);
      }
      else
      {
        builder.WithDescription("react to this post to register for the raid.");
      }
      builder.AddField("Raid info", $"next occurence: {appointment.next_occurence}");
      builder.WithFooter($"ID: {appointment.ID}");
      
      var message = channel.SendMessageAsync(embed: builder.Build()).Result;
      appointment.last_message_register = message;

      save();
    }

    public static void alert(IMessageChannel channel, RaidAppointment appointment)
    {
      var builder = new EmbedBuilder() {
        Title        = "Raid remainder",
        ThumbnailUrl = "https://wiki.guildwars2.com/images/5/5e/Commander_tag_%28green%29.png",
      };
      if(appointment.custom_message != null) builder.WithDescription(appointment.custom_message);
      var profession_list_builder = new StringBuilder(512);
      var unknown_professions_builder = new StringBuilder(512);
      appointment.last_message_register = channel.GetMessageAsync(appointment.last_message_register.Id).Result; //urgh
      foreach (var (emoji, metadata) in appointment.last_message_register.Reactions)
      {
        if(emoji is Emote emote && allowed_emotes.Contains(emote.Id))
        {
            profession_list_builder.Clear();
            foreach (var user in appointment.last_message_register.GetReactionUsersAsync(emoji, metadata.ReactionCount).FlattenAsync().Result)
            {
              if(profession_list_builder.Length > 1)
                profession_list_builder.Append('\n');
              profession_list_builder.Append(user.Mention);
            }
            builder.AddField(emote.ToString(), profession_list_builder);
        }
        else
        {
          foreach (var user in appointment.last_message_register.GetReactionUsersAsync(emoji, metadata.ReactionCount).FlattenAsync().Result)
          {
            if(unknown_professions_builder.Length > 1)
              unknown_professions_builder.Append('\n');
            unknown_professions_builder.Append(emoji).Append(' ').Append(user.Mention);
          }
        }
      }
      if(unknown_professions_builder.Length > 0)
      {
        builder.AddField("unknown profession", unknown_professions_builder);
      }
      builder.WithFooter($"ID: {appointment.ID}");
      appointment.last_message_alert = channel.SendMessageAsync(embed: builder.Build()).Result;

      save();
    }

    private static void save()
    {
      using var s = File.OpenWrite(SAVE_FILE);
      using var sw = new BinaryWriter(s);
      sw.Write(SAVE_VERSION);
      sw.Write(RaidAppointment.next_ID);
      sw.Write(context_dict.Count);
      foreach (var (guild, context) in context_dict)
      {
        sw.Write(guild.Id);
        sw.Write(context.Count);
        foreach (var (channel, appointments) in context)
        {
          sw.Write(channel.Id);
          sw.Write(appointments.Count);
          foreach (var appointment in appointments)
          {
            sw.Write(appointment.appointment_start.ToBinary());
            sw.Write(appointment.appointment_end.ToBinary());
            sw.Write(appointment.interval.ToString("c"));
            sw.Write(appointment.custom_message ?? string.Empty);
            sw.Write(appointment.ID);
            sw.Write(appointment.next_occurence.ToBinary());
            sw.Write(appointment.last_message_register?.Id ?? 0);
            sw.Write(appointment.last_message_alert?.Id ?? 0);
          }
        }
      }
    }
    private static bool load()
    {
      if(!File.Exists(SAVE_FILE)) return false;
      try
      {
        using var s = File.OpenRead(SAVE_FILE);
        using var sr = new BinaryReader(s);
        if(sr.ReadInt32() != SAVE_VERSION) return false; 
        RaidAppointment.next_ID = sr.ReadInt32();
        var n_guilds = sr.ReadInt32();
        context_dict.EnsureCapacity(n_guilds);
        for (int g = 0; g < n_guilds; g++)
        {
          var guild = client.GetGuild(sr.ReadUInt64());
          var n_channels = sr.ReadInt32();
          var context = new AppointmentContext(n_channels);
          for (int c = 0; c < n_channels; c++)
          {
            var channel = (ISocketMessageChannel)guild.GetChannel(sr.ReadUInt64());
            var n_appointments = sr.ReadInt32();
            var appointments = new List<RaidAppointment>(n_appointments);
            for (int a = 0; a < n_appointments; a++)
            {
              var appointment = new RaidAppointment();
              appointment.appointment_start     = DateTime.FromBinary(sr.ReadInt64());
              appointment.appointment_end       = DateTime.FromBinary(sr.ReadInt64());
              appointment.interval              = TimeSpan.ParseExact(sr.ReadString(), "c", CultureInfo.InvariantCulture);
              appointment.custom_message        = sr.ReadString();
              if(appointment.custom_message == string.Empty) appointment.custom_message = null;
              appointment.ID                    = sr.ReadInt32();
              appointment.next_occurence        = DateTime.FromBinary(sr.ReadInt64());
              var tmp = sr.ReadUInt64();
              if(tmp > 0) appointment.last_message_register = channel.GetMessageAsync(tmp).Result;
              tmp = sr.ReadUInt64();
              if(tmp > 0) appointment.last_message_alert = channel.GetMessageAsync(tmp).Result;
              
              appointments.Add(appointment);
            }
            context.Add(channel, appointments);
          }
          context_dict.Add(guild, context);
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine("Cant load\n" + ex);
        return false;
      }

      return true;
    }

    private static Task log(LogMessage msg)
    {
      Console.WriteLine(msg.ToString());
      return Task.CompletedTask;
    }
  }

  class RaidAppointment {
    public DateTime appointment_start;
    public DateTime appointment_end;
    public TimeSpan interval;
    public string?  custom_message;

    public int        ID;
    public static int next_ID;
    
    public DateTime  next_occurence;
    public IMessage? last_message_register;
    public IMessage? last_message_alert;
  }
}
