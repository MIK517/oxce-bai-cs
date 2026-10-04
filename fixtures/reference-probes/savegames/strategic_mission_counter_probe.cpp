// Verbatim AlienMission::ufoReachedWaypoint legacy wave inference with controlled counters.
#include <iostream>
#include <cstddef>
int infer(int saved,std::size_t _nextWave,std::size_t count){
 struct Ufo{int wave;int getMissionWaveNumber()const{return wave;}}ufo{saved};
 struct Rule{std::size_t count;std::size_t getWaveCount()const{return count;}}_rule{count};
 // INFER
 return waveNumber;
}
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::size_t count:{1,2,4})for(std::size_t next=0;next<=count;next++)for(int saved=-1;saved<(int)count;saved++){
  if(!first)std::cout<<',';first=false;std::cout<<'['<<count<<','<<next<<','<<saved<<','<<infer(saved,next,count)<<']';
 }std::cout<<"]}\n";
}
