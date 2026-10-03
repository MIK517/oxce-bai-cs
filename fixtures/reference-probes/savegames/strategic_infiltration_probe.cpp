#include <iostream>
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
 std::cout<<"]}\n";
}
