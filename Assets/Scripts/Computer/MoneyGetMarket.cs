using System;
using System.Collections.Generic;

namespace MoonThemedGame.Computer
{
    // Fictional in-game exchange. Prices and positions belong to this session, like the PC wallet.
    public sealed class MoneyGetMarket
    {
        public enum Failure { None, InvalidOrder, AlreadyOpen, NotEnoughCredits }
        public enum Exit { Manual, Liquidated, StopLoss, TakeProfit }
        public sealed class Quote
        {
            public MoneyGetStock definition;
            public double price,opening,previous;
            public readonly List<double> history=new List<double>();
        }
        public sealed class Position
        {
            public int stock,side,shares,leverage,margin;
            public double entry;
            public bool stopLoss,takeProfit;
        }
        public struct Settlement
        {
            public int stock,payout,profit;
            public Exit reason;
        }
        readonly Random random;
        readonly List<Position> positions=new List<Position>();
        public IReadOnlyList<Position> Positions=>positions;
        public Quote[] Quotes {get;}
        public int NewsStock {get;private set;}=-1;
        public bool NewsUp {get;private set;}
        int untilNews;
        public int RealizedProfit {get;private set;}

        public MoneyGetMarket(MoneyGetStock[] definitions,int seed)
        {
            random=new Random(seed);untilNews=random.Next(6,11);Quotes=new Quote[definitions.Length];
            for(int i=0;i<Quotes.Length;i++)
            {
                var def=definitions[i];var quote=new Quote{definition=def,price=def.startingPrice,opening=def.startingPrice};
                for(int h=0;h<48;h++){quote.history.Add(quote.price);quote.price=Price(quote.price*Math.Exp(Noise()*def.volatility*.65));}
                // Historic chart ends at the opening quote, without a fake first-tick jump.
                double factor=def.startingPrice/quote.price;
                for(int h=0;h<quote.history.Count;h++)quote.history[h]=Price(quote.history[h]*factor);
                quote.price=def.startingPrice;quote.previous=quote.price;quote.history.Add(quote.price);Quotes[i]=quote;
            }
        }
        double Noise()=>Math.Sqrt(-2*Math.Log(Math.Max(.000001,random.NextDouble())))*Math.Cos(2*Math.PI*random.NextDouble());
        static double Price(double value)=>Math.Round(Math.Max(.5,Math.Min(10000,value)),2);
        public Position Find(int stock)=>positions.Find(p=>p.stock==stock);
        public static int RequiredMargin(double price,int shares,int leverage)=>(int)Math.Ceiling(price*shares/leverage-1e-8);
        public int Equity(Position p)=>(int)Math.Max(0,Math.Floor(p.margin+(Quotes[p.stock].price-p.entry)*p.shares*p.side+1e-8));
        public int Profit(Position p)=>Equity(p)-p.margin;
        public double StopPrice(Position p)=>p.entry-p.side*p.margin*.20/p.shares;
        public double TakePrice(Position p)=>p.entry+p.side*p.margin*.35/p.shares;
        public Failure Open(int stock,int side,int shares,int leverage,bool stopLoss,bool takeProfit,int credits,out int debit)
        {
            debit=0;
            if(stock<0 || stock>=Quotes.Length || (side!=1 && side!=-1) || shares<1 || shares>100 || leverage<1 || leverage>3)return Failure.InvalidOrder;
            if(Find(stock)!=null)return Failure.AlreadyOpen;
            int margin=RequiredMargin(Quotes[stock].price,shares,leverage);
            if(credits<margin)return Failure.NotEnoughCredits;
            positions.Add(new Position{stock=stock,side=side,shares=shares,leverage=leverage,margin=margin,entry=Quotes[stock].price,stopLoss=stopLoss,takeProfit=takeProfit});
            debit=margin;return Failure.None;
        }
        public bool Close(int stock,Exit reason,out Settlement settlement)
        {
            settlement=default;var p=Find(stock);if(p==null)return false;
            int payout=Equity(p);
            settlement=new Settlement{stock=stock,payout=payout,profit=payout-p.margin,reason=reason};
            RealizedProfit+=settlement.profit;positions.Remove(p);return true;
        }
        public List<Settlement> Tick()
        {
            double common=Noise()*.012;int eventStock=-1;double shock=0;
            if(--untilNews<=0){eventStock=random.Next(Quotes.Length);NewsStock=eventStock;NewsUp=random.Next(2)==0;shock=(.12+random.NextDouble()*.14)*(NewsUp?1:-1);untilNews=random.Next(6,11);}
            for(int i=0;i<Quotes.Length;i++)
            {
                var q=Quotes[i];double momentum=Math.Max(-.04,Math.Min(.04,(q.price/q.previous-1)*.22));
                q.previous=q.price;q.price=Price(q.price*Math.Exp(common+Noise()*q.definition.volatility+momentum+(i==eventStock?shock:0)));
                q.history.Add(q.price);if(q.history.Count>96)q.history.RemoveAt(0);
            }
            var exits=new List<Settlement>();
            for(int i=positions.Count-1;i>=0;i--)
            {
                var p=positions[i];int equity=Equity(p);double raw=(Quotes[p.stock].price-p.entry)*p.shares*p.side;
                Exit? reason=equity<=0?Exit.Liquidated:p.stopLoss && raw<=-p.margin*.20?Exit.StopLoss:p.takeProfit && raw>=p.margin*.35?Exit.TakeProfit:null;
                if(reason.HasValue && Close(p.stock,reason.Value,out var closed))exits.Add(closed);
            }
            return exits;
        }
    }
}
