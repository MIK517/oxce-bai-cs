// Pins reference world-simulation arithmetic: target coordinates and distance, moving-target
// speed vectors and movement steps, region containment and random points, UFO heading and
// visibility, craft fuel range, base detection chance, weighted selection and alien-strategy
// mission location bookkeeping. Method bodies marked below are inserted verbatim by
// tools/capture-strategic-world-reference.ps1. RNG is scripted, not reproduced: the port
// injects its own source and only the selection arithmetic is pinned.
#include <algorithm>
#include <cassert>
#include <cfloat>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#define M_PI_2 1.57079632679489661923
#define M_PI_4 0.785398163397448309616
#endif

// FMATH_ARE_SAME
// FMATH_DEG2RAD
// FMATH_NAUTICAL
// FMATH_XCOM_DISTANCE

namespace RNG {
int intValue = 0;
double fraction = 0.0;
int generate(int min, int max) { return std::min(max, min + intValue); }
double generate(double min, double max) { return min + fraction * (max - min); }
}

namespace OpenXcom {

class Target {
public:
	double _lon = 0.0, _lat = 0.0;
	double getLongitude() const { return _lon; }
	void setLongitude(double lon);
	double getLatitude() const { return _lat; }
	void setLatitude(double lat);
	double getDistance(const Target *target) const { return getDistance(target->getLongitude(), target->getLatitude()); }
	double getDistance(double lon, double lat) const;
};
// TARGET_SET_LONGITUDE
// TARGET_SET_LATITUDE
// TARGET_GET_DISTANCE

class MovingTarget : public Target {
public:
	Target *_dest = nullptr;
	double _speedLon = 0.0, _speedLat = 0.0, _speedRadian = 0.0, _meetPointLon = 0.0, _meetPointLat = 0.0;
	int _speed = 0;
	bool _meetCalculated = false;
	std::vector<MovingTarget*> _followers;
	std::vector<MovingTarget*> *getFollowers() { return &_followers; }
	Target *getDestination() const { return _dest; }
	static double calculateRadianSpeed(int speed);
	void setSpeed(int speed);
	void calculateSpeed();
	void calculateMeetPoint();
	bool reachedDestination() const;
	void move();
	void resetMeetPoint() { _meetCalculated = false; }
};
// MOVING_TARGET_CALCULATE_RADIAN_SPEED
// MOVING_TARGET_SET_SPEED
// MOVING_TARGET_CALCULATE_SPEED
// MOVING_TARGET_CALCULATE_MEET_POINT
// MOVING_TARGET_REACHED_DESTINATION
// MOVING_TARGET_MOVE

struct MissionArea {
	double lonMin, lonMax, latMin, latMax;
	int texture = 0;
	std::string name;
	bool isPoint() const { return AreSame(lonMin, lonMax) && AreSame(latMin, latMax); }
};
struct MissionZone { std::vector<MissionArea> areas; };

class RuleRegion {
public:
	std::vector<double> _lonMin, _lonMax, _latMin, _latMax;
	std::vector<MissionZone> _missionZones;
	bool insideRegion(double lon, double lat, bool ignoreTechnicalRegion = false) const;
	std::pair<double, double> getRandomPoint(size_t zone, int area = -1) const;
};
// RULE_REGION_INSIDE_REGION
// RULE_REGION_GET_RANDOM_POINT

struct RuleUfoStats { int speedMax = 0, radarRange = 0, radarChance = 0, damageMax = 100; };
class RuleUfo {
public:
	int _visibility = 0;
	int getDefaultVisibility() const { return _visibility; }
};

class Ufo : public MovingTarget {
public:
	static const char *ALTITUDE_STRING[];
	const RuleUfo *_rules = nullptr;
	std::string _altitude = "STR_HIGH_UC";
	std::string _direction = "STR_NORTH";
	void calculateSpeed();
	int getVisibility() const;
	int getAltitudeInt() const;
};
const char *Ufo::ALTITUDE_STRING[] = { "STR_GROUND", "STR_VERY_LOW", "STR_LOW_UC", "STR_HIGH_UC", "STR_VERY_HIGH" };
// UFO_CALCULATE_SPEED
// UFO_GET_VISIBILITY
// UFO_GET_ALTITUDE_INT

struct RuleCraft { bool _refuelItem = false; bool getRefuelItem() const { return _refuelItem; } };
class Base;
class Craft : public MovingTarget {
public:
	RuleCraft *_rules = nullptr;
	RuleUfoStats _stats;
	Base *_base = nullptr;
	int _fuel = 0;
	double _speedMaxRadian = 0.0;
	void recalcSpeedMaxRadian();
	int getFuelConsumption(int speed, int escortSpeed) const;
	int getFuelLimit(Base *base) const;
	double getBaseRange() const;
	double getDistance(const Target *target) const { return Target::getDistance(target->getLongitude(), target->getLatitude()); }
};
class Base : public Target {
public:
	struct FacilityRules {
		int _sizeX = 1, _sizeY = 1, _mindShieldPower = 0;
		bool _mindShield = false;
		int getSizeX() const { return _sizeX; }
		int getSizeY() const { return _sizeY; }
		bool isMindShield() const { return _mindShield; }
		int getMindShieldPower() const { return _mindShieldPower; }
	};
	class Facility {
	public:
		FacilityRules _rules;
		int _buildTime = 0;
		bool _disabled = false;
		const FacilityRules *getRules() const { return &_rules; }
		int getBuildTime() const { return _buildTime; }
		bool getDisabled() const { return _disabled; }
	};
	std::vector<Facility*> _facilities;
	size_t getDetectionChance() const;
};
// BASE_GET_DETECTION_CHANCE
// CRAFT_RECALC_SPEED_MAX_RADIAN
// CRAFT_GET_FUEL_CONSUMPTION
// CRAFT_GET_FUEL_LIMIT
// CRAFT_GET_BASE_RANGE

class WeightedOptions {
public:
	std::map<std::string, size_t> _choices;
	size_t _totalWeight = 0;
	std::string choose() const;
	void set(const std::string &id, size_t weight);
	bool empty() const { return 0 == _totalWeight; }
};
// WEIGHTED_OPTIONS_CHOOSE
// WEIGHTED_OPTIONS_SET

class AlienStrategy {
public:
	std::map<std::string, std::vector<std::pair<std::string, int> > > _missionLocations;
	std::map<std::string, int> _missionRuns;
	void addMissionRun(const std::string &varName, int increment = 1);
	void addMissionLocation(const std::string &varName, const std::string &regionName, int zoneNumber, int maximum);
	bool validMissionLocation(const std::string &varName, const std::string &regionName, int zoneNumber);
};
// ALIEN_STRATEGY_ADD_MISSION_RUN
// ALIEN_STRATEGY_ADD_MISSION_LOCATION
// ALIEN_STRATEGY_VALID_MISSION_LOCATION

}

using namespace OpenXcom;

static std::string fixed12(double value)
{
	std::ostringstream stream;
	stream << std::fixed << std::setprecision(12) << value;
	return stream.str();
}

int main()
{
	std::cout << std::fixed << std::setprecision(12);
	std::cout << R"({"schemaVersion":1,"referenceCommit":"4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15")"
		<< R"(,"referenceBuild":{"compiler":"MSVC","languageStandard":"c++20"},"mods":[])";

	// Coordinate wrapping and great-circle distance.
	std::cout << ",\"coordinates\":[";
	bool first = true;
	for (double lon : { -0.5, 0.0, 6.0, 7.0, 12.6 })
		for (double lat : { -2.0, -1.4, 0.0, 1.4, 2.0 })
		{
			Target target;
			// Reference order of MovingTarget::move: longitude first, then latitude,
			// so a latitude past a pole flips the longitude that was just set.
			target.setLongitude(lon);
			target.setLatitude(lat);
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << fixed12(lon) << ',' << fixed12(lat) << ','
				<< fixed12(target.getLongitude()) << ',' << fixed12(target.getLatitude()) << ']';
		}
	std::cout << "],\"distances\":[";
	first = true;
	for (auto pair : { std::make_pair(0.0, 0.0), std::make_pair(1.0, 0.5), std::make_pair(3.14, -1.0) })
	{
		Target from; from.setLongitude(0.4); from.setLatitude(0.2);
		if (!first) std::cout << ','; first = false;
		std::cout << '[' << fixed12(pair.first) << ',' << fixed12(pair.second) << ','
			<< fixed12(from.getDistance(pair.first, pair.second)) << ','
			<< XcomDistance(from.getDistance(pair.first, pair.second)) << ']';
	}

	// Speed vector and movement steps toward a fixed destination.
	std::cout << "],\"movement\":[";
	first = true;
	for (int speed : { 0, 400, 3200 })
	{
		Target destination; destination.setLongitude(1.0); destination.setLatitude(0.6);
		MovingTarget mover; mover.setLongitude(0.4); mover.setLatitude(0.2);
		mover._dest = &destination;
		mover.setSpeed(speed);
		std::vector<std::string> steps;
		for (int step = 0; step < 3; ++step)
		{
			mover.move();
			steps.push_back(fixed12(mover.getLongitude()) + "," + fixed12(mover.getLatitude()));
		}
		if (!first) std::cout << ','; first = false;
		std::cout << '[' << speed << ',' << fixed12(mover._speedRadian) << ',' << fixed12(mover._speedLon)
			<< ',' << fixed12(mover._speedLat) << ',' << mover.reachedDestination();
		for (const auto& step : steps) std::cout << ',' << step;
		std::cout << ']';
	}

	// Region containment, including the wrapped-longitude and pole rules.
	std::cout << "],\"regions\":[";
	first = true;
	{
		RuleRegion region;
		region._lonMin = { Deg2Rad(350.0), Deg2Rad(10.0) };
		region._lonMax = { Deg2Rad(20.0), Deg2Rad(40.0) };
		region._latMin = { Deg2Rad(-10.0), Deg2Rad(30.0) };
		region._latMax = { Deg2Rad(10.0), Deg2Rad(90.0) };
		RuleRegion technical;
		for (double lonDeg : { 0.0, 15.0, 25.0, 355.0 })
			for (double latDeg : { -10.0, 0.0, 30.0, 90.0 })
			{
				if (!first) std::cout << ','; first = false;
				std::cout << '[' << fixed12(lonDeg) << ',' << fixed12(latDeg) << ','
					<< region.insideRegion(Deg2Rad(lonDeg), Deg2Rad(latDeg)) << ','
					<< technical.insideRegion(Deg2Rad(lonDeg), Deg2Rad(latDeg)) << ','
					<< technical.insideRegion(Deg2Rad(lonDeg), Deg2Rad(latDeg), true) << ']';
			}
	}

	// Random points inside a zone: bounds are normalized, the fraction comes from the caller.
	std::cout << "],\"randomPoints\":[";
	first = true;
	{
		RuleRegion region;
		MissionZone zone;
		zone.areas.push_back({ Deg2Rad(20.0), Deg2Rad(10.0), Deg2Rad(5.0), Deg2Rad(-5.0), 3, "" });
		zone.areas.push_back({ Deg2Rad(40.0), Deg2Rad(40.0), Deg2Rad(15.0), Deg2Rad(15.0), 7, "CITY" });
		region._missionZones.push_back(zone);
		for (double fraction : { 0.0, 0.25, 1.0 })
			for (int area : { -1, 0, 1 })
			{
				RNG::fraction = fraction;
				RNG::intValue = 1;
				auto point = region.getRandomPoint(0, area);
				if (!first) std::cout << ','; first = false;
				std::cout << '[' << fixed12(fraction) << ',' << area << ',' << fixed12(point.first)
					<< ',' << fixed12(point.second) << ']';
			}
		if (!first) std::cout << ',';
		std::cout << '[' << region._missionZones[0].areas[0].isPoint() << ','
			<< region._missionZones[0].areas[1].isPoint() << ']';
	}

	// UFO heading, altitude index and visibility.
	std::cout << "],\"ufo\":[";
	first = true;
	for (auto destination : { std::make_pair(0.4, 0.2), std::make_pair(1.0, 0.2), std::make_pair(0.4, 0.9),
		std::make_pair(1.0, 0.9), std::make_pair(0.1, 0.05) })
	{
		Target target; target.setLongitude(destination.first); target.setLatitude(destination.second);
		Ufo ufo; ufo.setLongitude(0.4); ufo.setLatitude(0.2);
		ufo._dest = &target;
		ufo._speedRadian = MovingTarget::calculateRadianSpeed(2000);
		ufo.calculateSpeed();
		if (!first) std::cout << ','; first = false;
		std::cout << "[\"" << ufo._direction << "\"]";
	}
	std::cout << "],\"visibility\":[";
	first = true;
	{
		RuleUfo rules; rules._visibility = 15;
		for (const char *altitude : { "STR_GROUND", "STR_VERY_LOW", "STR_LOW_UC", "STR_HIGH_UC", "STR_VERY_HIGH" })
		{
			Ufo ufo; ufo._rules = &rules; ufo._altitude = altitude;
			if (!first) std::cout << ','; first = false;
			std::cout << "[\"" << altitude << "\"," << ufo.getAltitudeInt() << ',' << ufo.getVisibility() << ']';
		}
	}

	// Craft fuel consumption, low-fuel limit and patrol range.
	std::cout << "],\"craft\":[";
	first = true;
	for (bool refuelItem : { false, true })
		for (int speedMax : { 760, 2100 })
			for (int fuel : { 0, 60, 1000 })
			{
				RuleCraft rules; rules._refuelItem = refuelItem;
				Base base; base.setLongitude(0.4); base.setLatitude(0.2);
				Craft craft; craft._rules = &rules; craft._base = &base; craft._fuel = fuel;
				craft._stats.speedMax = speedMax;
				craft.recalcSpeedMaxRadian();
				craft.setLongitude(1.0); craft.setLatitude(0.6);
				if (!first) std::cout << ','; first = false;
				std::cout << '[' << refuelItem << ',' << speedMax << ',' << fuel << ','
					<< craft.getFuelConsumption(speedMax, 0) << ',' << craft.getFuelConsumption(speedMax, 300) << ','
					<< craft.getFuelLimit(&base) << ',' << fixed12(craft.getBaseRange()) << ','
					<< fixed12(craft._speedMaxRadian) << ']';
			}

	// Base detection chance and the radar detection chance arithmetic of Base::detect.
	std::cout << "],\"detection\":[";
	first = true;
	for (int shields : { 0, 1, 2 })
		for (int facilities : { 1, 7 })
		{
			Base base;
			std::vector<Base::Facility> storage;
			storage.reserve(facilities + shields + 1);
			for (int index = 0; index < facilities; ++index) storage.push_back({ { 2, 2, 0, false }, 0, false });
			for (int index = 0; index < shields; ++index) storage.push_back({ { 1, 1, 1, true }, 0, false });
			storage.push_back({ { 1, 1, 3, true }, 5, false }); // under construction, ignored
			for (auto& facility : storage) base._facilities.push_back(&facility);
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << shields << ',' << facilities << ',' << (int)base.getDetectionChance() << ']';
		}
	std::cout << "],\"radarChance\":[";
	first = true;
	for (int radarChance : { 10, 60 })
		for (int visibility : { -30, 0, 15 })
		{
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << radarChance << ',' << visibility << ','
				<< radarChance * (100 + visibility) / 100 << ']';
		}

	// Weighted selection and alien-strategy mission bookkeeping.
	std::cout << "],\"weighted\":[";
	first = true;
	{
		for (int roll = 1; roll <= 7; ++roll)
		{
			WeightedOptions options;
			options.set("STR_ALPHA", 3);
			options.set("STR_BETA", 4);
			options.set("STR_GAMMA", 0);
			RNG::intValue = roll - 1;
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << roll << ",\"" << options.choose() << "\"," << (int)options._totalWeight << ']';
		}
		WeightedOptions removed;
		removed.set("STR_ALPHA", 3);
		removed.set("STR_ALPHA", 0);
		std::cout << ",[0,\"" << removed.choose() << "\"," << (int)removed._totalWeight << ']';
	}
	std::cout << "],\"missionLocations\":[";
	{
		AlienStrategy strategy;
		strategy.addMissionRun("varA");
		strategy.addMissionRun("varA", 2);
		strategy.addMissionRun("", 5);
		strategy.addMissionLocation("varA", "REGION_A", 1, 0);
		std::cout << '[' << (int)strategy._missionLocations.size() << ','
			<< strategy.validMissionLocation("varA", "REGION_A", 1) << ']';
		strategy.addMissionLocation("varA", "REGION_A", 1, 2);
		strategy.addMissionLocation("varB", "REGION_B", 0, 2);
		std::cout << ",[" << (int)strategy._missionLocations.size() << ','
			<< strategy.validMissionLocation("varA", "REGION_A", 1) << ','
			<< strategy.validMissionLocation("varA", "REGION_A", 2) << ','
			<< strategy._missionRuns["varA"] << ',' << (int)strategy._missionRuns.count("") << ']';
		strategy.addMissionLocation("varA", "REGION_C", 2, 2);
		strategy.addMissionLocation("varA", "REGION_D", 3, 2);
		std::cout << ",[" << (int)strategy._missionLocations.size() << ','
			<< (int)strategy._missionLocations["varA"].size() << ','
			<< (int)strategy._missionLocations.count("varB") << ','
			<< strategy.validMissionLocation("varA", "REGION_A", 1) << ']';
	}

	// Trajectory speed percentage, spawn countdown and shot-down delay arithmetic.
	std::cout << "],\"trajectory\":[";
	first = true;
	for (int percentage : { 0, 50, 100, 150 })
		for (int speedMax : { 0, 3100 })
		{
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << percentage << ',' << speedMax << ',' << speedMax * percentage / 100 << ']';
		}
	std::cout << "],\"countdown\":[";
	first = true;
	for (size_t spawnTimer : { (size_t)0, (size_t)9000, (size_t)15000 })
		for (size_t roll : { (size_t)0, (size_t)7 })
		{
			size_t scaled = spawnTimer / 30;
			size_t countdown = (scaled / 2 + std::min(roll, scaled)) * 30;
			if (!first) std::cout << ','; first = false;
			std::cout << '[' << (int)spawnTimer << ',' << (int)roll << ',' << (int)scaled << ',' << (int)countdown << ']';
		}
	std::cout << "]}" << std::endl;
	return 0;
}
