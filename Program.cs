﻿namespace TetrisAI;

using FastConsole;
using System.Windows.Input;
using Tetris;

// Inputs: standard height, caves, pillars, row transitions, col transitions, trash
// Outputs: score of state
// TODO:
//- Do timekeeping on a separate thread
//- Separate lines sent and lines cleared
//- add height of trash as a feature
//- add a featrue for t-spins
class Program
{
    const int MAX_LINES = 500, THINK_TIME_IN_MILLIS = 150;
    const double PLACE_COE = 0;
    static readonly string Population_path = AppDomain.CurrentDomain.BaseDirectory + @"Pops\new vs.txt";
        
    static void Main()
    {
        // Set up console
        Console.Title = "Tetris NEAT AI Training";
        FConsole.Framerate = 15;
        FConsole.CursorVisible = false;
        FConsole.SetFont("Consolas", 18);
        FConsole.Initialise(FrameEndCallback);

        //Game[] games = new Game[2].Select(x => new Game()).ToArray();
        //Game.SetGames(games);
        //SetupPlayerInput(games[0]);

        // Train NNs
        NN.Train(Population_path, FitnessFunction, 6, 1, 30);
    }

    static void SetupPlayerInput(Game player_game)
    {
        player_game.SoftG = 40;
        FConsole.AddOnPressListener(Key.Left, () => player_game.Play(Moves.Left));
        //FastConsole.AddOnHoldListener(Key.Left, () => main.Play(Moves.Left), 133, 0);
        FConsole.AddOnHoldListener(Key.Left, () => player_game.Play(Moves.DASLeft), 133, 15);

        FConsole.AddOnPressListener(Key.Right, () => player_game.Play(Moves.Right));
        //FastConsole.AddOnHoldListener(Key.Right, () => main.Play(Moves.Right), 133, 0);
        FConsole.AddOnHoldListener(Key.Right, () => player_game.Play(Moves.DASRight), 133, 15);

        FConsole.AddOnPressListener(Key.Up, () => player_game.Play(Moves.RotateCW));
        FConsole.AddOnPressListener(Key.Z, () => player_game.Play(Moves.RotateCCW));
        FConsole.AddOnPressListener(Key.A, () => player_game.Play(Moves.Rotate180));

        FConsole.AddOnHoldListener(Key.Down, () => player_game.Play(Moves.SoftDrop), 0, 16);
        FConsole.AddOnPressListener(Key.Space, () => player_game.Play(Moves.HardDrop));

        FConsole.AddOnPressListener(Key.C, () => player_game.Play(Moves.Hold));
        FConsole.AddOnPressListener(Key.R, () => player_game.Restart());
        FConsole.AddOnPressListener(Key.Escape, () => Game.IsPaused = !Game.IsPaused);
    }

    static void FrameEndCallback()
    {
        // Wait for games to be ready
        while (!Game.AllReady()) Thread.Sleep(0);
        // Aquire lock
        while (Interlocked.Exchange(ref Game.GamesLock, 1) == 1) Thread.Sleep(0);

        foreach (Game g in Game.Games) g.TickAsync();

        // Release lock
        Interlocked.Exchange(ref Game.GamesLock, 0);
    }

    static void FitnessFunction(NN[] networks, int gen, double compat_tresh)
    {
        // Pick up where we left off
        int current = 0;
        while (networks[current].Played)
        {
            current++;
            if (current == networks.Length) return;
        }

        // Round-robin tournament
        for (int i = current; i < networks.Length - 1; i++)
        {
            // Pick up where we left off
            int rival = i + 1;
            while (networks[rival].Played)
            {
                rival++;
                if (rival == networks.Length) break;
            }
            
            // Play against rest of networks
            for (int j = rival; j < networks.Length; j++)
            {
                // Prepare games
                int seed = Guid.NewGuid().GetHashCode();
                Game[] games = new Game[2].Select(x => new Game(5, seed)).ToArray();
                Game.SetGames(games);

                // Show info on console
                var played = networks.TakeWhile(x => x.Played);
                FConsole.Set(FConsole.Width, FConsole.Height + 8);
                FConsole.CursorVisible = false;
                FConsole.WriteAt($"Gen: {gen}", 1, 28);
                FConsole.WriteAt($"Best: {(!played.Any() ? 0 : played.Max(x => x.Fitness))}", 1, 29);
                FConsole.WriteAt($"Average Fitness: {(!played.Any() ? 0 : played.Average(x => x.Fitness))}", 1, 30);
                FConsole.WriteAt($"Average Size: {networks.Average(x => x.GetSize())}", 1, 31);
                FConsole.WriteAt($"No. of Species: {NN.Speciate(networks, compat_tresh).Count}", 1, 32);

                // Start bots
                Bot left = new Bot(networks[i], games[0]);
                Bot right = new Bot(networks[j], games[1]);
                left.Game.Name = "AI no. " + i;
                right.Game.Name = "AI no. " + j;
                Game.IsPaused = false;
                left.Start(THINK_TIME_IN_MILLIS, 0);
                right.Start(THINK_TIME_IN_MILLIS, 0);

                // Wait until one dies or game takes too long
                while (games.All(x => !x.IsDead) && games.All(x => x.Lines < MAX_LINES))
                {
                    foreach (Game g in Game.Games)
                    {
                        g.WriteAt(0, 6, ConsoleColor.White, $"Sent: {g.Sent}".PadRight(11));
                        g.WriteAt(0, 22, ConsoleColor.White, $"APL: {Math.Round(g.APL, 3)}".PadRight(10));
                    }
                    Thread.Sleep((int)(1000 / FConsole.Framerate));
                }
                
                // Stop bots
                Game.IsPaused = true;
                left.Stop();
                right.Stop();

                // Experiment with this
                networks[i].Fitness += Math.Max(0, (left.Game.IsDead ? left.Game.Sent - right.Game.Sent : left.Game.APL * MAX_LINES - right.Game.Sent) + (left.Game.PiecesPlaced * 0.4 * PLACE_COE));
                networks[j].Fitness += Math.Max(0, (right.Game.IsDead ? right.Game.Sent - left.Game.Sent : right.Game.APL * MAX_LINES - left.Game.Sent) + (right.Game.PiecesPlaced * 0.4 * PLACE_COE));
                // Save progress
                networks[j].Played = true;
                NN.SaveNNs(Population_path, networks, gen, compat_tresh);
            }
            
            for (int j = i + 1; j < networks.Length; j++)
                networks[j].Played = false;
            networks[i].Played = true;
            networks[i].Fitness /= networks.Length - 1;
        }
        networks[^1].Played = true;
        networks[^1].Fitness /= networks.Length - 1;

        return;
    }
}

