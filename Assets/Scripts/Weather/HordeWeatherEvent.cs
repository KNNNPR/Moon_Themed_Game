using System.Collections.Generic;
using MoonThemedGame.Interactions;
using MoonThemedGame.Repair;
using MoonThemedGame.Scavenger;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonThemedGame.Weather
{
    // Owned by the Operator weather, never by the PC game's additive arena.
    [DisallowMultipleComponent]
    public sealed class HordeWeatherEvent : MonoBehaviour
    {
        readonly List<HordeZombie> enemies=new List<HordeZombie>();
        readonly RaycastHit[] hits=new RaycastHit[64];
        MoonWeatherSystem weather;
        ScavengerLink link;
        GameObject prefab;
        Light glow;
        float nextSpawn,glowAmount;
        bool running;
        public int Spawned {get;private set;}
        public int AliveCount {get{int count=0;foreach(var enemy in enemies)if(enemy&&!enemy.Actor.IsDead&&!enemy.Retreating)count++;return count;}}
        public void Initialize(MoonWeatherSystem owner)
        {
            weather=owner;prefab=Resources.Load<GameObject>("Zombie");
            weather.WeatherChanged+=Changed;Changed(weather.Current);
            var host=new GameObject("Horde / amber surface glow");host.transform.SetParent(transform,false);
            glow=host.AddComponent<Light>();glow.type=LightType.Point;glow.color=new Color(1,.19f,.035f);
            glow.shadows=LightShadows.None;glow.range=80;glow.intensity=0;glow.enabled=false;
        }
        void Changed(WeatherKind kind)
        {
            running=kind==WeatherKind.Horde;nextSpawn=Time.time+.8f;
            if(!running)foreach(var enemy in enemies)if(enemy)enemy.Retreat();
        }
        void Update()
        {
            if(!weather)return;
            if(!link)link=FindFirstObjectByType<ScavengerLink>();
            enemies.RemoveAll(enemy=>!enemy);
            float target=running?weather.Intensity:0;
            glowAmount=Mathf.MoveTowards(glowAmount,target,Time.deltaTime/3);
            if(glow)
            {
                var focus=link&&link.robot?link.robot.position:weather.shelterCenter;
                glow.transform.position=focus+Vector3.up*14;
                glow.intensity=2200*glowAmount*(.94f+.06f*Mathf.Sin(Time.time*.7f));
                glow.enabled=glowAmount>.001f&&!weather.IsSheltered(focus);
            }
            if(!running||!prefab||!link||!link.state||!link.robot||!link.state.HasSignal||
                weather.IsBaseFootprint(link.robot.position,4)||GameSettingsMenu.BlockGameplayInput||DeveloperConsole.BlockGameplayInput)return;
            if(Time.time<nextSpawn||AliveCount>=weather.maximumHordeZombies||enemies.Count>=weather.maximumHordeZombies*2)return;
            nextSpawn=Time.time+Mathf.Max(.5f,weather.hordeSpawnInterval);
            if(TryFindSpawn(out var point))SpawnAt(point);
        }
        public HordeZombie SpawnAt(Vector3 point)
        {
            if(!running||!prefab||!link||!link.state||weather.IsBaseFootprint(point,3))return null;
            float unit=Mathf.Max(.1f,PlayerBody.Scale);
            var direction=Vector3.ProjectOnPlane(link.robot.position-point,Vector3.up);
            var actor=ZombieSpawner.Create(prefab,point,Quaternion.LookRotation(direction.sqrMagnitude>.01f?direction:Vector3.forward),unit);
            if(!actor)return null;
            SceneManager.MoveGameObjectToScene(actor.gameObject,gameObject.scene);
            actor.name="Horde / Cosmonaut";actor.targetOverride=link.robot;actor.detectionRange=2000/unit;
            actor.walkSpeed=1.2f;actor.attackReach=.15f;actor.corpseLifetime=12;
            var enemy=actor.gameObject.AddComponent<HordeZombie>();enemy.Initialize(this,weather,link,actor);
            enemies.Add(enemy);Spawned++;return enemy;
        }
        bool TryFindSpawn(out Vector3 point)
        {
            float unit=Mathf.Max(.1f,PlayerBody.Scale);var focus=link.robot.position;
            for(int attempt=0;attempt<20;attempt++)
            {
                var circle=Random.insideUnitCircle.normalized*Random.Range(16,30)*unit;
                var candidate=focus+new Vector3(circle.x,0,circle.y);
                if(weather.IsBaseFootprint(candidate,5)||!weather.MeteorMapBounds.Contains(new Vector3(candidate.x,weather.MeteorMapBounds.center.y,candidate.z)))continue;
                int count=Physics.RaycastNonAlloc(candidate+Vector3.up*20*unit,Vector3.down,hits,80*unit,weather.groundLayers,QueryTriggerInteraction.Ignore);
                float nearest=float.MaxValue;int selected=-1;
                for(int i=0;i<count;i++)
                {
                    var hit=hits[i];
                    if(hit.normal.y<.75f||hit.distance>=nearest||hit.collider.GetComponentInParent<ScavengerRobotState>()||
                        hit.collider.GetComponentInParent<ZombieActor>()||hit.collider.GetComponentInParent<FirstPersonController>())continue;
                    nearest=hit.distance;selected=i;
                }
                if(selected<0)continue;
                point=hits[selected].point+Vector3.up*(.04f*unit);
                if(Mathf.Abs(point.y-focus.y)>12*unit||weather.IsSheltered(point))continue;
                if(Physics.CheckCapsule(point+Vector3.up*.4f*unit,point+Vector3.up*1.5f*unit,.3f*unit,~0,QueryTriggerInteraction.Ignore))continue;
                bool occupied=false;foreach(var enemy in enemies)if(enemy&&Vector3.Distance(enemy.transform.position,point)<2*unit){occupied=true;break;}
                if(!occupied)return true;
            }
            point=default;return false;
        }
        void OnDisable()
        {
            running=false;if(glow)glow.enabled=false;
            foreach(var enemy in enemies)if(enemy)enemy.Retreat();
        }
        void OnDestroy()
        {
            if(weather)weather.WeatherChanged-=Changed;
            foreach(var enemy in enemies)if(enemy)Destroy(enemy.gameObject);
        }
    }
}
