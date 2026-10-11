#include <iostream>
#include <vector>
#include <algorithm>
namespace RNG {
 int calls=0;
 int generate(int minimum,int){++calls;return minimum;}
 template<typename T>
 // SHUFFLE
}

struct Country {
 bool _pact, _newPact, _cancelPact;
 bool canBeInfiltrated();
 void setNewPact();
};
// ELIGIBILITY
// PACT
int main() {
 std::cout << "{\"defaults\":[false,false],\"countries\":[";
 bool first=true;
 for(bool pact:{false,true})for(bool pending:{false,true})for(bool cancel:{false,true}) {
  if(!first)std::cout<<',';first=false;
  Country country{pact,pending,cancel};bool eligible=country.canBeInfiltrated();
  country.setNewPact();
  std::cout<<'['<<pact<<','<<pending<<','<<cancel<<','<<eligible<<','<<country._pact<<','<<country._newPact<<','<<country._cancelPact<<']';
 }
 std::cout<<"],\"shuffle\":["; first=true;
 for(int count=0;count<=6;++count) {
  std::vector<int> values;for(int i=0;i<count;++i)values.push_back(i);
  RNG::calls=0;RNG::shuffle(values);
  if(!first)std::cout<<',';first=false;
  std::cout<<'['<<count<<','<<RNG::calls<<",[";
  for(int i=0;i<count;++i){if(i)std::cout<<',';std::cout<<values[i];}
  std::cout<<"]]";
 }
 std::cout<<"]}\n";
}
