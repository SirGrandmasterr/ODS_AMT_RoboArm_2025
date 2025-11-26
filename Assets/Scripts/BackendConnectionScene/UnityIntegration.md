# Unity XR Integration Guide

This guide explains how to integrate the provided C# scripts into your Unity XR project to enable Authentication and Scenario Selection.

## Prerequisites
- Unity 2021.3+ (Recommended)
- **Newtonsoft.Json** package (Install via Package Manager > Add package from git URL > `com.unity.nuget.newtonsoft-json`)
- **TextMeshPro** (Import TMP Essentials)

## Setup Instructions

### 1. Scene Setup
1.  Create a new Scene (e.g., `LoginScene`).
2.  Create an empty GameObject named `AuthManager` and attach the `AuthManager.cs` script.
    - Set `Base Url` to your backend URL (e.g., `http://localhost:8000` or your server IP).
3.  Create a UI Canvas (XR compatible if using VR, e.g., World Space Canvas).

### 2. UI Configuration
Create the following UI hierarchy under your Canvas:

- **LoginPanel** (Panel)
    - Username Input (TMP_InputField)
    - Password Input (TMP_InputField)
    - Login Button (Button)
    - Register Button (Button)
    - Status Text (TextMeshProUGUI)
- **RegisterPanel** (Panel) - *Initially Inactive*
    - Username Input (TMP_InputField)
    - Password Input (TMP_InputField)
    - Confirm Password Input (TMP_InputField)
    - Register Button (Button)
    - Back to Login Button (Button)
    - Status Text (TextMeshProUGUI)
- **ScenarioPanel** (Panel) - *Initially Inactive*
    - Scenario 1 Button (Button)
    - Scenario 2 Button (Button)
    - Scenario 3 Button (Button)

### 3. Script Attachment
1.  Create an empty GameObject named `UIManager` (or attach to Canvas).
2.  Attach `LoginUIManager.cs`.
3.  Drag and drop the UI references from the Hierarchy to the script fields in the Inspector.
4.  Attach `ScenarioSelector.cs` to the `ScenarioPanel` or `UIManager`.
5.  Drag and drop the Scenario Buttons to the script fields.
6.  Configure the `Scene Names` in the Inspector to match your actual scene names.

## Usage
1.  Run the Backend Server.
2.  Play the Unity Scene.
3.  Enter credentials and click Login (or Register).
4.  Upon success, the Scenario Panel will appear.
5.  Clicking a Scenario button will attempt to load that scene.
