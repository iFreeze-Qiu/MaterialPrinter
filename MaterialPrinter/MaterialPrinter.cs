using System;
using System.Collections.Generic;
using System.Collections;

using UnityEngine;
using KSPPluginFramework;

namespace MaterialPrinter
{
	public class PrintedProductMaterials : IConfigNode
	{
		[KSPField(isPersistant = true)]
		public string requiredResourceName;
		[KSPField(isPersistant = true)]
		public float requiredResourceAmount;

		public void Load(ConfigNode node)
		{
			if (node.HasValue ("requiredResourceName"))
				requiredResourceName = node.GetValue ("requiredResourceName");
			if (node.HasValue ("requiredResourceAmount"))
				requiredResourceAmount = float.Parse(node.GetValue ("requiredResourceAmount"));
		}

		public void Save(ConfigNode node)
		{
			node.AddValue ("requiredResourceName", requiredResourceName);
			node.AddValue ("requiredResourceAmount", requiredResourceAmount);
		}

		public override string ToString()
		{
			string stringRepresentation = "";
			stringRepresentation = String.Format("{0},{1:F2}", requiredResourceName, requiredResourceAmount);
			return stringRepresentation;
		}
		public static PrintedProductMaterials FromString(string s)
		{
			PrintedProductMaterials productMaterials = null;
			string[] sections = s.Split(new char[1] { ',' });
			if (sections.Length == 2)
			{
				productMaterials = new PrintedProductMaterials();
				productMaterials.requiredResourceName = sections [0];
				productMaterials.requiredResourceAmount = float.Parse(sections[1]);
			}
			return productMaterials;
		}
	}

	public class PrintedProduct : IConfigNode
	{
		[KSPField(isPersistant = true)]
		public string productName;
		[KSPField(isPersistant = true)]
		public List<PrintedProductMaterials> requiredMaterials;

		public void Load(ConfigNode node)
		{
			if (node.HasValue ("productName"))
				productName = node.GetValue ("productName");
			if (requiredMaterials == null)
				requiredMaterials = new List<PrintedProductMaterials> ();
			requiredMaterials.Clear ();
			foreach (ConfigNode materialNode in node.GetNodes("PRINTED_PRODUCT_MATERIALS")) {
				PrintedProductMaterials materials = new PrintedProductMaterials ();
				materials.Load (materialNode);
				requiredMaterials.Add (materials);
			}
		}

		public void Save(ConfigNode node)
		{
			node.AddValue ("productName", productName);
			foreach (PrintedProductMaterials materials in requiredMaterials) {
				ConfigNode materialNode = node.AddNode("PRINTED_PRODUCT_MATERIALS");
				materials.Save (materialNode);
			}
		}
	
		public override string ToString()
		{
			string stringRepresentation = "";
			stringRepresentation = String.Format("{0}", productName);
			foreach (PrintedProductMaterials materials in requiredMaterials) {
				stringRepresentation = stringRepresentation + "|" + materials.ToString ();
			}
			return stringRepresentation;
		}
		public void FromString(string s)
		{
			if (requiredMaterials == null)
				requiredMaterials = new List<PrintedProductMaterials> ();
			string[] sections = s.Split(new char[1] { '|' });
			productName = sections[0];
			for (int i = 1; i < sections.Length; i++) {
				if (sections [i].Trim ().Length == 0)
					continue;
				PrintedProductMaterials materials = PrintedProductMaterials.FromString (sections [i]);
				requiredMaterials.Add (materials);
			}
		}

	}

	public class MaterialPrinter : PartModuleWindow
	{
		public List<PrintedProduct> availableProducts;

		public List<string> availableProductsPacked;

		[KSPEvent(guiActive = true, guiName = "Toggle Printer Panel")]
		public void Print()
		{
			Visible = !Visible;
		}

		public override void OnAwake ()
		{
			if (availableProducts == null)
				availableProducts = new List<PrintedProduct> ();
			if (availableProductsPacked == null)
				availableProductsPacked = new List<string> ();
			base.OnAwake ();
		}

		public override void OnStart (StartState state)
		{
			// when starting we need to re-load our data from the packed strings
			// because for some reason KSP/Unity will dump the more complex datastructures from memory
			if (availableProducts == null || availableProducts.Count == 0) {
				availableProducts = new List<PrintedProduct> ();
				foreach (string packedString in availableProductsPacked) {
					PrintedProduct product = new PrintedProduct ();
					product.FromString (packedString);
					availableProducts.Add (product);
				}
			}
			base.OnStart (state);
		}

		public override void OnLoad (ConfigNode node)
		{
			if (node.HasNode ("PRINTED_PRODUCT")) {
				if (availableProducts == null)
					availableProducts = new List<PrintedProduct> ();
				availableProducts.Clear ();
				foreach (ConfigNode productNode in node.GetNodes("PRINTED_PRODUCT")) {
					PrintedProduct product = new PrintedProduct ();
					product.Load (productNode);
					availableProducts.Add (product);
					availableProductsPacked.Add (product.ToString ());
				}
			}
			base.OnLoad (node);
		}

		public override void OnSave (ConfigNode node)
		{
			if (availableProducts == null) {
				base.OnSave (node);
				return;
			}
			foreach (PrintedProduct product in availableProducts) {
				ConfigNode productNode = node.AddNode("PRINTED_PRODUCT");
				product.Save (productNode);
			}
			base.OnSave (node);
		}

		public void CreateProduct(PrintedProduct product, int amount)
		{
			float toMake = (float)amount;
			Debug.Log ("MaterialPrinter: Attempting to print " + product.productName);
			// Look that all materials are available
			foreach (PrintedProductMaterials mat in product.requiredMaterials) {
				List<PartResource> availableResources = new List<PartResource> ();
				this.part.GetConnectedResources (PartResourceLibrary.Instance.GetDefinition(mat.requiredResourceName).id, ResourceFlowMode.ALL_VESSEL, availableResources);
				float totalAmountAvailable = 0f;
				foreach (PartResource res in availableResources) {
					totalAmountAvailable += (float)res.amount;
				}
				if (totalAmountAvailable < mat.requiredResourceAmount * toMake) {
					Debug.Log ("MaterialPrinter: Not enough " + mat.requiredResourceName);
					return;
				}
			}
			// look that we have a place to store the output
			List<PartResource> finalResource = new List<PartResource> ();
			int finalResourceID = PartResourceLibrary.Instance.GetDefinition (product.productName).id;
			this.part.GetConnectedResources (finalResourceID, ResourceFlowMode.ALL_VESSEL, finalResource);
			foreach (PartResource res in finalResource) {
				if (res.maxAmount - res.amount >= toMake) {
					// print the product
					foreach (PrintedProductMaterials mat in product.requiredMaterials) {
						float got = this.part.RequestResource (mat.requiredResourceName, mat.requiredResourceAmount * toMake);
						if (got < mat.requiredResourceAmount * toMake) {
							// didn't get enough resources!
							Debug.Log ("MaterialPrinter: Failed to request enough " + mat.requiredResourceName);
							return;
						}
					}
					res.part.RequestResource (product.productName, -(toMake));
					Debug.Log ("MaterialPrinter: Printed " + product.productName);
					return;
				}
			}
			Debug.Log ("MaterialPrinter: Failed to find a place to store final product");
		}

		internal override void OnGUIOnceOnly ()
		{
			GUISkin skinDefault = SkinsLibrary.CopySkin(SkinsLibrary.DefSkinType.Unity);
			SkinsLibrary.AddSkin ("Default", skinDefault);
			SkinsLibrary.SetCurrent ("Default");
			base.OnGUIOnceOnly ();
		}

		internal override void DrawWindow (int id)
		{
			if (availableProducts == null) {
				Visible = false;
				return;
			}
			float windowHeight = availableProducts.Count * 20;
			WindowRect = new Rect (10, 10, 500, windowHeight);
			GUILayout.BeginVertical ();
			foreach (PrintedProduct product in availableProducts) {
				GUILayout.BeginHorizontal ();
				GUILayout.Label (String.Format ("{0,50}", product.productName));
				if (GUILayout.Button ("1")) {
					CreateProduct (product, 1);
				}
				if (GUILayout.Button ("5")) {
					CreateProduct (product, 5);
				}
				if (GUILayout.Button ("10")) {
					CreateProduct (product, 10);
				}
				if (GUILayout.Button ("15")) {
					CreateProduct (product, 15);
				}
				if (GUILayout.Button ("25")) {
					CreateProduct (product, 25);
				}
				if (GUILayout.Button ("50")) {
					CreateProduct (product, 50);
				}
				if (GUILayout.Button ("100")) {
					CreateProduct (product, 100);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical ();
		}
	}

//	public class MaterialBin : PartModule
//	{
//	}
}

