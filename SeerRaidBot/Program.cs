using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Commands.Builders;
using Discord.WebSocket;

namespace SeerRaidBot {
  class Program {
    static DiscordSocketClient client;
    static CommandService      commands;

    //https://discord.com/api/oauth2/authorize?client_id=866727722835116033&permissions=2048&scope=bot%20applications.commands
    static void Main(string[] args) {
      string token = File.ReadAllText("token.txt");

      client = new DiscordSocketClient();
      client.Log += log;

      client.MessageReceived += handle_message;
      var config = new CommandServiceConfig(); //todo
      commands = new CommandService(config); //todo
      var result = commands.AddModulesAsync(Assembly.GetEntryAssembly(), null).Result;

      client.LoginAsync(TokenType.Bot, token).Wait();
      client.StartAsync().Wait();
      
      if (!Console.IsInputRedirected)
      {
        while (Console.ReadLine() != "exit")
          Console.WriteLine("write 'exit' to exit.");
      }
      else
      {
        Thread.Sleep(Timeout.Infinite);
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
      good:;

      int start_index = 0;

      if(!user_message.HasCharPrefix('/', ref start_index)) return;

      var context = new SocketCommandContext(client, user_message);
      var result = commands.ExecuteAsync(context, start_index, null).Result;
      if(!result.IsSuccess)
      {
        Console.WriteLine(result.ErrorReason);
        Console.WriteLine(result.Error);
      }
    }

    private static Task log(LogMessage msg)
    {
      Console.WriteLine(msg.ToString());
      return Task.CompletedTask;
    }
  }

  [Group("raid")]
  public class Commands : ModuleBase<SocketCommandContext> {

    [Command("add")]
    [Summary("for example: /raid add so19:00-21:30 7 this raid starts on sunday 7pm, runs for 2.5h and repeats every 7 days")]
    public async Task add(string first_occurance, int interval, [Remainder] string custom_text)
    {
      var day = (first_occurance[..2].ToLower()) switch
      {
        "mo" => DayOfWeek.Monday,
        "tu" => DayOfWeek.Tuesday,
        "we" => DayOfWeek.Wednesday,
        "th" => DayOfWeek.Thursday,
        "fr" => DayOfWeek.Friday,
        "sa" => DayOfWeek.Saturday,
        "su" => DayOfWeek.Sunday,
      };
      var start_time = first_occurance.Substring(2, 5);
      var   end_time = first_occurance.Substring(8, 5);;
      var now = DateTime.Now;
      var days = (day - now.DayOfWeek + 7) % 7;

      var start_day = now.AddDays(days);
      var start_timestamp = new DateTime(
        start_day.Year, start_day.Month, start_day.Day, 
        int.Parse(start_time[..2]), int.Parse(start_time[3..]), 0
      );
      var interval_span = TimeSpan.FromDays(interval);

      Context.Channel.SendMessageAsync("oh boy").Wait();
    }
  }
}
