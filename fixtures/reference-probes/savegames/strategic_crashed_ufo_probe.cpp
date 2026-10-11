#include <cassert>
#include <iostream>
#include <vector>
struct Ufo {
 enum Status { FLYING, LANDED, CRASHED, DESTROYED, IGNORE_ME };
 Status _status = CRASHED; int _secondsRemaining; bool _detected;
 void think(); void move() {} bool reachedDestination() { return false; }
 bool isHunting() { return false; } bool isEscorting() { return false; } void setSpeed(int) {}
 Status getStatus() { return _status; } void setStatus(Status status) { _status = status; }
 int getSecondsRemaining() { return _secondsRemaining; } void setSecondsRemaining(int value) { _secondsRemaining = value; }
 void setDetected(bool value) { _detected = value; }
};
// THINK
struct SavedGame { std::vector<Ufo*> ufos; auto* getUfos() { return &ufos; } };
struct Game { SavedGame save; SavedGame* getSavedGame() { return &save; } };
void halfHour(Game* _game) { // EXPIRY
}
void fiveSeconds(Ufo* ufo) {
 switch (ufo->getStatus()) { // CRASHED_CASE
 default: break;
 }
}
int main() {
 std::cout << "{\"rows\":["; bool first = true;
 for (int seconds : {0,1,1799,1800,1801,3600}) for (int half : {0,1}) for (int detected : {0,1}) {
  if (!first) std::cout << ','; first = false;
  Ufo ufo{Ufo::CRASHED,seconds,bool(detected)}; Game game{{{&ufo}}};
  if (half) halfHour(&game); fiveSeconds(&ufo);
  std::cout << '[' << seconds << ',' << half << ',' << detected << ',' << int(ufo._status) << ',' << ufo._secondsRemaining << ',' << ufo._detected << ']';
 }
 std::cout << "]}\n";
}
